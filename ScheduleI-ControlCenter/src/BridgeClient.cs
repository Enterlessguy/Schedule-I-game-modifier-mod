using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace ScheduleIControlCenter
{
    internal sealed class BridgeClient
    {
        public const string PipeName = "ScheduleI.ControlBridge.v1";
        private const int MaxResponseCharacters = 256 * 1024;
        private readonly object invokeLock = new object();

        public Task<OperationResult> InvokeAsync(string operation, Dictionary<string, object> arguments, bool dryRun)
        {
            return Task.Run(() => Invoke(operation, arguments, dryRun));
        }

        public OperationResult Invoke(string operation, Dictionary<string, object> arguments, bool dryRun)
        {
            lock (invokeLock)
                return InvokeCore(operation, arguments, dryRun);
        }

        private OperationResult InvokeCore(string operation, Dictionary<string, object> arguments, bool dryRun)
        {
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    pipe.Connect(1000);
                    if (pipe.CanTimeout)
                    {
                        pipe.ReadTimeout = 15000;
                        pipe.WriteTimeout = 5000;
                    }
                    using (StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true))
                    using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
                    {
                        writer.AutoFlush = true;
                        string requestId = Guid.NewGuid().ToString("N");
                        Dictionary<string, object> request = new Dictionary<string, object>
                        {
                            { "v", 1 },
                            { "id", requestId },
                            { "op", operation },
                            { "args", arguments ?? new Dictionary<string, object>() },
                            { "dryRun", dryRun }
                        };
                        writer.WriteLine(JsonUtil.CreateSerializer().Serialize(request));
                        string line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line))
                            return Failure("bridge_closed", "The live bridge closed without a response.", DiagnosticCategory.Bridge);
                        if (line.Length > MaxResponseCharacters)
                            return Failure("response_too_large", "The live bridge response exceeded the 256 KiB client limit.", DiagnosticCategory.Protocol);

                        Dictionary<string, object> response = JsonUtil.AsObject(JsonUtil.CreateSerializer().DeserializeObject(line));
                        if (response == null)
                            return Failure("invalid_response", "The live bridge returned a non-object response.", DiagnosticCategory.Protocol);
                        if (JsonUtil.GetInt(response, "v", 0) != 1)
                            return Failure("protocol_version", "The live bridge returned an unsupported protocol version.", DiagnosticCategory.Protocol);
                        if (!string.Equals(JsonUtil.GetString(response, "id", string.Empty), requestId, StringComparison.Ordinal))
                            return Failure("request_mismatch", "The live bridge returned a mismatched request id.", DiagnosticCategory.Protocol);
                        string responseCode = JsonUtil.GetString(response, "code", "bridge_error");
                        string responseMessage = JsonUtil.GetString(response, "message", "The live bridge returned no message.");
                        long responseRevision = JsonUtil.GetInt(response, "revision", 0);
                        Dictionary<string, object> responseData = JsonUtil.AsObject(response.ContainsKey("data") ? response["data"] : null) ?? new Dictionary<string, object>();
                        if (!JsonUtil.GetBool(response, "ok", false))
                        {
                            OperationResult failure = OperationResult.Fail(string.Format("Bridge error [{0}]: {1}", responseCode, responseMessage));
                            failure.Code = responseCode;
                            failure.Revision = responseRevision;
                            failure.Data = responseData;
                            failure.RawResponse = line;
                            DiagnosticsService.RecordFailure(failure, "bridge." + operation, DiagnosticCategory.Bridge, "Response code=" + responseCode + "; revision=" + responseRevision);
                            return failure;
                        }
                        OperationResult success = OperationResult.Ok(responseMessage);
                        success.Code = responseCode;
                        success.Revision = responseRevision;
                        success.Data = responseData;
                        success.RawResponse = line;
                        return success;
                    }
                }
            }
            catch (TimeoutException)
            {
                return Failure("timeout", "Live bridge is not connected.", DiagnosticCategory.Bridge);
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "bridge." + operation, DiagnosticCategory.Bridge, DiagnosticSeverity.Error, "The live bridge request failed.", null);
                return OperationResult.Fail("Bridge request failed: " + ex.Message, ex);
            }
        }

        private static OperationResult Failure(string code, string message, DiagnosticCategory category)
        {
            OperationResult result = OperationResult.Fail(message);
            result.Code = code;
            DiagnosticsService.RecordFailure(result, "bridge", category, "Failure code=" + code);
            return result;
        }
    }
}
