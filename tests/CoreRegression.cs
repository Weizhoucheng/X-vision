using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;
using VisionInspect.Core.Services;
using VisionInspect.Core.Utilities;
using VisionInspect.Infrastructure.Logging;
using VisionInspect.Infrastructure.Processing;
using VisionInspect.Infrastructure.Security;
using VisionInspect.Infrastructure.Storage;

// Hardware independent regression tests. The fakes only supply external boundaries;
// the state machine, image conversion, result pipeline and user/log stores are production code.
internal static class CoreRegression
{
    private static readonly TestLogger Logger = new TestLogger();
    private static int _passed;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static async Task Bounded(Task task)
    {
        Assert(await Task.WhenAny(task, Task.Delay(3000)) == task, "Test operation timed out");
        await task;
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new Exception("Condition timed out");
            await Task.Delay(5);
        }
    }
    private static async Task Check(string name, Func<Task> test)
    {
        await test();
        _passed++;
        Console.WriteLine("PASS " + name);
    }
    public static int Main(string[] args)
    {
        try { Run(args[0]).GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static async Task Run(string directory)
    {
        await Check("paused gate responds to cancellation", async () =>
        {
            var gate = new AsyncManualResetEvent();
            using (var cts = new CancellationTokenSource())
            {
                Task waiting = gate.WaitAsync(cts.Token);
                cts.Cancel();
                Assert(await Task.WhenAny(waiting, Task.Delay(1000)) == waiting, "Cancellation registration was disposed too early");
                try { await waiting; throw new Exception("Cancellation was ignored"); }
                catch (OperationCanceledException) { }
            }
            gate.Set(); await Bounded(gate.WaitAsync());
            gate.Reset(); Assert(!gate.IsSet, "Reset failed");
        });
        await Check("RGB/BGR/Mono color and padded row conversion", () =>
        {
            using (var bitmap = new Bitmap(3, 2, PixelFormat.Format24bppRgb))
            {
                bitmap.SetPixel(0, 0, Color.Red);
                bitmap.SetPixel(2, 1, Color.Blue);
                RawImage raw = RawImage.FromBitmap(bitmap);
                Assert(raw.Format == ImagePixelFormat.Rgb24 && raw.Data[0] == 255 && raw.Data[2] == 0, "GDI BGR was mislabeled RGB");
                Assert(raw.Data[15] == 0 && raw.Data[17] == 255, "Padded row conversion failed");
                using (Bitmap roundTrip = raw.ToBitmap())
                    Assert(roundTrip.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "RGB preview swaps red and blue");
            }
            using (Bitmap bgr = new RawImage(new byte[] { 0, 0, 255 }, 1, 1, ImagePixelFormat.Bgr24).ToBitmap())
                Assert(bgr.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "BGR preview swaps red and blue");
            using (var indexed = new Bitmap(1, 1, PixelFormat.Format8bppIndexed))
            {
                ColorPalette palette = indexed.Palette;
                palette.Entries[1] = Color.Red; indexed.Palette = palette;
                BitmapData bits = indexed.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                try { Marshal.WriteByte(bits.Scan0, 1); } finally { indexed.UnlockBits(bits); }
                RawImage colored = RawImage.FromBitmap(indexed);
                Assert(colored.Format == ImagePixelFormat.Rgb24 && colored.Data[0] == 255 && colored.Data[2] == 0,
                    "Indexed color palette was treated as grayscale");
            }
            using (Bitmap mono = new RawImage(new byte[] { 127 }, 1, 1, ImagePixelFormat.Mono8).ToBitmap())
                Assert(mono.GetPixel(0, 0).R == 127, "Mono conversion failed");
            try { new RawImage(new byte[1], int.MaxValue, 2, ImagePixelFormat.Rgb24); throw new Exception("Image size overflow accepted"); }
            catch (OverflowException) { }
            return Task.CompletedTask;
        });
        await Check("stop preserves a result already returned by vision", async () =>
        {
            var vision = new FakeVision(); var sink = new TestSink();
            var controller = Controller(vision, sink, 1000);
            await controller.StartAsync(DetectionMode.Continuous, new TestInput());
            await Bounded(vision.Started.Task);
            Task stop = controller.StopAsync();
            await Until(() => vision.Token.IsCancellationRequested);
            vision.Result.TrySetResult(Result());
            await Bounded(stop);
            Assert(sink.Count == 1 && !sink.LastToken.CanBeCanceled, "Completed detection was lost on Stop");
            Assert(controller.State == DetectionRunState.Idle, "Stop did not reach Idle");
        });
        await Check("pause keeps next acquisition blocked and stop exits", async () =>
        {
            var vision = new FakeVision(); var sink = new TestSink(); var input = new TestInput();
            var controller = Controller(vision, sink, 1000);
            await controller.StartAsync(DetectionMode.Continuous, input);
            await Bounded(vision.Started.Task); await controller.PauseAsync();
            vision.Result.TrySetResult(Result()); await Until(() => sink.Count == 1);
            await Task.Delay(50);
            Assert(input.Count == 1 && controller.State == DetectionRunState.Paused, "Pause allowed another acquisition");
            await Bounded(controller.StopAsync());
        });
        await Check("stop timeout propagates and a later retry drains", async () =>
        {
            var vision = new FakeVision(); var sink = new TestSink();
            var controller = Controller(vision, sink, 30);
            await controller.StartAsync(DetectionMode.Single, new TestInput());
            await Bounded(vision.Started.Task);
            try { await controller.StopAsync(); throw new Exception("Stop timeout was swallowed"); }
            catch (TimeoutException) { }
            Assert(controller.State == DetectionRunState.Fault, "Timeout must fault");
            try { await controller.StartAsync(DetectionMode.Single, new TestInput()); throw new Exception("Concurrent run was allowed"); }
            catch (VisionInspectException) { }
            vision.Result.TrySetResult(Result());
            await Bounded(controller.StopAsync());
            Assert(sink.Count == 1, "Retry lost the outstanding result");
            await controller.ShutdownAsync();
        });
        await Check("single inspection failure never silently retries product", async () =>
        {
            var vision = new FakeVision(); vision.Result.TrySetException(new IOException("Injected vision fault"));
            var controller = Controller(vision, new TestSink(), 1000);
            await controller.StartAsync(DetectionMode.Single, new TestInput());
            await Until(() => controller.State == DetectionRunState.Fault);
            Assert(vision.Calls == 1, "Single mode retried inspection");
            await controller.StopAsync();
        });
        await Check("dropped and NG-only archives do not log phantom paths", async () =>
        {
            var repository = new TestRepository(); var archive = new TestArchive();
            var storage = new StorageSettings { EnableDetectionLog = true, EnableImageArchive = true, ArchiveNgOnly = false };
            var pipeline = new ResultProcessingPipeline(repository, archive, null, Logger, storage);
            DetectionContext context = Context(false);
            await pipeline.HandleAsync(context, CancellationToken.None);
            Assert(archive.Attempts == 1 && repository.Record.ImagePath == string.Empty && repository.Record.Error.Length > 0,
                "Queue rejection logged a nonexistent image path");
            storage.ArchiveNgOnly = true;
            await pipeline.HandleAsync(Context(true), CancellationToken.None);
            Assert(archive.Attempts == 1 && repository.Record.ImagePath == string.Empty && repository.Record.Error == string.Empty,
                "NG-only policy logged an OK image");
        });
        await Check("report reads while daily log is open for write", () =>
        {
            string logDir = Path.Combine(directory, "daily");
            var repository = new DetectionLogRepository(logDir, Logger);
            DetectionRecord record = Context(true).Record; repository.Append(record);
            string path = Path.Combine(logDir, "detection-" + record.Timestamp.ToString("yyyy-MM-dd") + ".csv");
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                Assert(repository.Query(record.Timestamp.Date, record.Timestamp.Date.AddDays(1)).Count == 1,
                    "Daily log query conflicts with concurrent append handle");
            return Task.CompletedTask;
        });
        await Check("corrupt user store fails closed and preserves evidence", () =>
        {
            string path = Path.Combine(directory, "invalid-users.json");
            File.WriteAllText(path, "{corrupt");
            try { new AuthService(path, Logger); throw new Exception("Corrupt user store restored default administrator"); }
            catch (VisionInspectException) { }
            Assert(File.ReadAllText(path) == "{corrupt", "Corrupt user store was overwritten");
            return Task.CompletedTask;
        });
        await Check("report includes history older than 366 days", () =>
        {
            var repository = new DetectionLogRepository(Path.Combine(directory, "history"), Logger);
            DetectionRecord old = Context(false).Record;
            old.Timestamp = DateTime.Now.AddDays(-800);
            repository.Append(old);
            repository.Append(Context(true).Record);
            Assert(repository.Query(old.Timestamp.Date, DateTime.Now.AddDays(1)).Count == 2,
                "Long interval silently truncated report history");
            return Task.CompletedTask;
        });
        await Check("real archive memory budget rejects oversize frame without pending work", async () =>
        {
            string archiveRoot = Path.Combine(directory, "budget-images");
            using (var archive = new ImageArchiveService(archiveRoot, "png", false, 10, Logger, 7, maxPendingBytes: 1))
            {
                var repository = new TestRepository();
                var storage = new StorageSettings { EnableDetectionLog = true, EnableImageArchive = true, ArchiveNgOnly = false };
                var pipeline = new ResultProcessingPipeline(repository, archive, null, Logger, storage);
                DetectionContext context = Context(false);
                context.Image = new RawImage(new byte[] { 0, 255 }, 2, 1, ImagePixelFormat.Mono8);
                await pipeline.HandleAsync(context, CancellationToken.None);
                Assert(repository.Record.ImagePath == string.Empty && repository.Record.Error.Length > 0,
                    "Oversize frame rejection retained a phantom archive path");
                Task flush = archive.FlushAsync(CancellationToken.None);
                Assert(flush.IsCompleted, "Rejected frame was counted as pending work");
                await flush;
                Assert(Directory.GetFiles(archiveRoot, "*", SearchOption.AllDirectories).Length == 0,
                    "Rejected oversize frame wrote an image");
            }
        });
        Console.WriteLine("PASS TOTAL " + _passed);
    }
    private static VisionInspectionResult Result() { return new VisionInspectionResult { IsOk = true, Timestamp = DateTime.Now }; }
    private static DetectionController Controller(FakeVision vision, TestSink sink, int stopTimeout)
    {
        return new DetectionController(vision, sink, new StatisticsService(), Logger,
            new DetectionSettings { IntervalMs = 0, StopTimeoutMs = stopTimeout },
            new VisionSettings { PrimaryProcedureName = "Test" }, new ResultJudgeOptions());
    }
    private static DetectionContext Context(bool ok)
    {
        return new DetectionContext { Image = new RawImage(new byte[] { 128 }, 1, 1, ImagePixelFormat.Mono8),
            Inspection = new VisionInspectionResult { IsOk = ok },
            Record = new DetectionRecord { Timestamp = DateTime.Now, IsOk = ok, Sequence = 1 } };
    }
    private sealed class TestLogger : IAppLogger
    {
        public event EventHandler<LogEntryEventArgs> EntryWritten { add { } remove { } }
        public void Debug(string a, string m) { } public void Info(string a, string m) { }
        public void Warn(string a, string m) { } public void Error(string a, string m, Exception e = null) { }
        public void Audit(string u, string a, string d) { } public void Flush() { }
    }
    private sealed class TestInput : IInputProvider
    {
        public int Count; public string Name { get { return "Test"; } }
        public Task<RawImage> AcquireAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Interlocked.Increment(ref Count); return Task.FromResult(Context(true).Image); }
    }
    private sealed class TestSink : IResultSink
    {
        public int Count; public CancellationToken LastToken;
        public Task HandleAsync(DetectionContext c, CancellationToken token)
        { LastToken = token; Interlocked.Increment(ref Count); return Task.CompletedTask; }
    }
    private sealed class TestRepository : IDetectionLogRepository
    {
        public DetectionRecord Record;
        public void Append(DetectionRecord record) { Record = record; }
        public IReadOnlyList<DetectionRecord> Query(DateTime f, DateTime t) { return new[] { Record }; }
    }
    private sealed class TestArchive : IImageArchiveService
    {
        public int Attempts;
        public string BuildImagePath(DetectionRecord r) { return "planned.png"; }
        public bool Enqueue(DetectionRecord r, RawImage image) { Attempts++; return false; }
        public Task FlushAsync(CancellationToken token) { return Task.CompletedTask; }
    }
    private sealed class FakeVision : IVisionEngine
    {
        public readonly TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<VisionInspectionResult> Result = new TaskCompletionSource<VisionInspectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token; public int Calls;
        public bool IsLoaded { get { return true; } }
        public string LoadedSolutionPath { get { return "Test.sol"; } }
        public VisionSolutionInfo CurrentSolution { get { return new VisionSolutionInfo { PrimaryProcedureName = "Test" }; } }
        public IReadOnlyList<VisionModuleInfo> Modules { get { return new VisionModuleInfo[0]; } }
        public Task<VisionInspectionResult> RunOnceAsync(RawImage i, string p, ResultJudgeOptions j, CancellationToken t)
        { Token = t; Interlocked.Increment(ref Calls); Started.TrySetResult(true); return Result.Task; }
        public Task<VisionSolutionInfo> LoadSolutionAsync(string p, string w, CancellationToken t) { throw new NotSupportedException(); }
        public Task UnloadSolutionAsync(CancellationToken t) { throw new NotSupportedException(); }
        public Task SaveSolutionAsync(CancellationToken t) { throw new NotSupportedException(); }
        public Task<object> GetRenderSourceAsync(string p, CancellationToken t) { throw new NotSupportedException(); }
        public Task<object> GetModuleSourceAsync(string p, CancellationToken t) { throw new NotSupportedException(); }
        public Task<IReadOnlyList<VisionParameterInfo>> GetParametersAsync(string p, CancellationToken t) { throw new NotSupportedException(); }
        public Task<ParameterApplyResult> SetParameterAsync(string m, string p, string v, bool s, CancellationToken t) { throw new NotSupportedException(); }
        public Task<ParameterSnapshot> CaptureParametersAsync(string m, CancellationToken t) { throw new NotSupportedException(); }
        public Task<ParameterApplyResult> ApplyParametersAsync(string m, ParameterSnapshot p, bool s, CancellationToken t) { throw new NotSupportedException(); }
        public Task SaveParameterPresetAsync(string m, string p, CancellationToken t) { throw new NotSupportedException(); }
        public Task<ParameterApplyResult> LoadParameterPresetAsync(string m, string p, bool s, CancellationToken t) { throw new NotSupportedException(); }
        public Task<ParameterApplyResult> ResetParametersToDefaultAsync(string m, bool s, CancellationToken t) { throw new NotSupportedException(); }
        public Task ShutdownAsync() { return Task.CompletedTask; }
    }
}
