using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScheduleIControlBridge
{
    internal sealed class BridgeRequest
    {
        private readonly ManualResetEventSlim completed = new ManualResetEventSlim(false);
        // 0=queued, 1=executing on the Unity main thread, 2=complete, 3=canceled.
        private int state;

        public string Id { get; set; }
        public string Operation { get; set; }
        public JObject Arguments { get; set; }
        public bool DryRun { get; set; }
        public string Response { get; private set; }

        public bool TryBeginExecution()
        {
            return Interlocked.CompareExchange(ref state, 1, 0) == 0;
        }

        public bool TryCancelBeforeExecution()
        {
            return Interlocked.CompareExchange(ref state, 3, 0) == 0;
        }

        public void Complete(string response)
        {
            Response = response;
            Volatile.Write(ref state, 2);
            completed.Set();
        }

        public void CompleteWithoutExecution(string response)
        {
            if (Interlocked.CompareExchange(ref state, 2, 0) != 0)
                return;
            Response = response;
            completed.Set();
        }

        public bool Wait(int milliseconds)
        {
            return completed.Wait(milliseconds);
        }
    }

    internal sealed class PipeServer
    {
        public const string PipeName = "ScheduleI.ControlBridge.v1";
        private const int MaxRequestBytes = 16 * 1024;
        private const int RequestIoMilliseconds = 5000;
        private const int ResponseWaitMilliseconds = 10000;

        private static readonly HashSet<string> AllowedOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            "system.status",
            "system.compatibility.enable",
            "game.save",
            "product.price.list",
            "product.price.previewScale",
            "product.price.applyPreview",
            "sale.dealLimit.get",
            "sale.dealLimit.preview",
            "sale.dealLimit.applyPreview",
            "product.market.list",
            "product.market.previewSync",
            "product.market.applyPreview",
            "customer.allowance.list",
            "customer.allowance.preview",
            "customer.allowance.applyPreview",
            "business.launder.list",
            "business.launder.preview",
            "business.launder.applyPreview",
            "effects.list",
            "effects.preview",
            "effects.applyPreview",
            "player.settings.get",
            "player.settings.preview",
            "player.settings.applyPreview",
            "property.own"
        };

        private readonly Action<BridgeRequest> enqueue;
        private readonly Action<string> warn;
        private readonly object pipeLock = new object();
        private Thread worker;
        private NamedPipeServerStream activePipe;
        private volatile bool stopping;

        public PipeServer(Action<BridgeRequest> enqueue, Action<string> warn)
        {
            this.enqueue = enqueue;
            this.warn = warn;
        }

        public void Start()
        {
            worker = new Thread(Run)
            {
                IsBackground = true,
                Name = "ScheduleIControlBridge.Pipe"
            };
            worker.Start();
        }

        public void Stop()
        {
            stopping = true;
            lock (pipeLock)
            {
                if (activePipe != null)
                {
                    try { activePipe.Dispose(); }
                    catch { }
                    activePipe = null;
                }
            }

            if (worker != null && worker.IsAlive)
                worker.Join(1500);
        }

        private void Run()
        {
            while (!stopping)
            {
                try
                {
                    using (NamedPipeServerStream pipe = NamedPipeServerStreamAcl.Create(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        4096,
                        4096,
                        CreatePipeSecurity(),
                        HandleInheritability.None,
                        (PipeAccessRights)0))
                    {
                        lock (pipeLock)
                            activePipe = pipe;

                        pipe.WaitForConnection();
                        if (stopping)
                            return;

                        string line = ReadBoundedLine(pipe);
                        BridgeRequest request;
                        string immediateResponse;
                        if (!TryParseRequest(line, out request, out immediateResponse))
                        {
                            WriteLine(pipe, immediateResponse);
                            continue;
                        }

                        enqueue(request);
                        if (!request.Wait(ResponseWaitMilliseconds))
                        {
                            if (request.TryCancelBeforeExecution())
                            {
                                WriteLine(pipe, ProtocolJson.Response(
                                    request.Id,
                                    false,
                                    "main_thread_timeout",
                                    "The game main thread did not answer in time; the request was canceled before execution.",
                                    0,
                                    null));
                                continue;
                            }

                            // Execution already began. Wait for its authoritative response
                            // instead of claiming a mutation failed while it may still commit.
                            if (!request.Wait(20000))
                            {
                                WriteLine(pipe, ProtocolJson.Response(
                                    request.Id,
                                    false,
                                    "execution_uncertain",
                                    "Execution began but did not finish in time. Inspect status and the audit log before retrying.",
                                    0,
                                    null));
                                continue;
                            }
                        }

                        WriteLine(pipe, request.Response);
                    }
                }
                catch (ObjectDisposedException)
                {
                    if (!stopping)
                        warn("Named pipe was disposed unexpectedly; recreating it.");
                }
                catch (OperationCanceledException)
                {
                    if (!stopping)
                        warn("Named pipe client did not complete request/response I/O within 5 seconds; closing the connection.");
                }
                catch (IOException ex)
                {
                    if (!stopping)
                        warn("Named pipe I/O error: " + ex.Message);
                }
                catch (Exception ex)
                {
                    if (!stopping)
                        warn("Named pipe worker error: " + ex.Message);
                }
                finally
                {
                    lock (pipeLock)
                        activePipe = null;
                }
            }
        }

        private static PipeSecurity CreatePipeSecurity()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity == null || identity.User == null)
                throw new InvalidOperationException("Could not determine the bridge owner's Windows SID.");

            PipeSecurity security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                identity.User,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));
            SecurityIdentifier interactiveUsers = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);
            security.AddAccessRule(new PipeAccessRule(
                interactiveUsers,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
            return security;
        }

        private static string ReadBoundedLine(Stream stream)
        {
            byte[] oneByte = new byte[1];
            using (CancellationTokenSource deadline = new CancellationTokenSource(RequestIoMilliseconds))
            using (MemoryStream bytes = new MemoryStream())
            {
                while (true)
                {
                    int count = stream.ReadAsync(oneByte, 0, 1, deadline.Token).GetAwaiter().GetResult();
                    if (count == 0)
                        throw new EndOfStreamException("Client disconnected before sending a complete request.");
                    int value = oneByte[0];
                    if (value == '\n')
                        break;
                    if (value == '\r')
                        continue;
                    if (bytes.Length >= MaxRequestBytes)
                        throw new InvalidDataException("Request exceeded the 16 KiB limit.");
                    bytes.WriteByte((byte)value);
                }

                return new UTF8Encoding(false, true).GetString(bytes.ToArray());
            }
        }

        private static void WriteLine(Stream stream, string value)
        {
            byte[] data = new UTF8Encoding(false, true).GetBytes((value ?? string.Empty) + "\n");
            using (CancellationTokenSource deadline = new CancellationTokenSource(RequestIoMilliseconds))
            {
                stream.WriteAsync(data, 0, data.Length, deadline.Token).GetAwaiter().GetResult();
                stream.FlushAsync(deadline.Token).GetAwaiter().GetResult();
            }
        }

        private static bool TryParseRequest(string line, out BridgeRequest request, out string response)
        {
            request = null;
            response = null;
            string id = string.Empty;

            try
            {
                JObject root;
                JsonLoadSettings settings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    CommentHandling = CommentHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Ignore
                };
                using (StringReader text = new StringReader(line))
                using (JsonTextReader json = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                {
                    root = JObject.Load(json, settings);
                    if (json.Read())
                        return Reject(id, "invalid_json", "The request contained trailing JSON or text.", out response);
                }
                id = root.Value<string>("id") ?? string.Empty;
                int? version = root.Value<int?>("v");
                string operation = root.Value<string>("op") ?? string.Empty;
                JToken dryRunToken = root["dryRun"];
                JObject arguments = root["args"] as JObject;

                if (version != 1)
                    return Reject(id, "unsupported_version", "Only protocol version 1 is supported.", out response);
                if (!IsSafeIdentifier(id, 64))
                    return Reject(string.Empty, "invalid_id", "Request id is missing or invalid.", out response);
                if (!AllowedOperations.Contains(operation))
                    return Reject(id, "operation_not_allowed", "The requested operation is not allowlisted.", out response);
                if (arguments == null)
                    return Reject(id, "invalid_args", "args must be a JSON object.", out response);
                if (dryRunToken == null || dryRunToken.Type != JTokenType.Boolean)
                    return Reject(id, "invalid_dry_run", "dryRun must be a JSON boolean.", out response);

                request = new BridgeRequest
                {
                    Id = id,
                    Operation = operation,
                    Arguments = arguments,
                    DryRun = dryRunToken.Value<bool>()
                };
                return true;
            }
            catch (JsonException)
            {
                return Reject(id, "invalid_json", "The request was not valid JSON.", out response);
            }
            catch (Exception)
            {
                return Reject(id, "invalid_json", "The request contained invalid field types or values.", out response);
            }
        }

        private static bool Reject(string id, string code, string message, out string response)
        {
            response = ProtocolJson.Response(id, false, code, message, 0, null);
            return false;
        }

        internal static bool IsSafeIdentifier(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxLength)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                    return false;
            }
            return true;
        }
    }

    internal static class ProtocolJson
    {
        public static string Response(
            string id,
            bool ok,
            string code,
            string message,
            long revision,
            JObject data)
        {
            JObject response = new JObject
            {
                ["v"] = 1,
                ["id"] = id ?? string.Empty,
                ["ok"] = ok,
                ["code"] = code ?? string.Empty,
                ["message"] = message ?? string.Empty,
                ["revision"] = revision,
                ["data"] = data ?? new JObject()
            };
            return response.ToString(Formatting.None);
        }
    }
}
