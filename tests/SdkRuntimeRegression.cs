using System;
using System.IO;
using System.Threading;
using VisionInspect.Core;
using VisionInspect.VisionMaster.Runtime;
using XVision.Sdk;

internal static class SdkRuntimeRegression
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    [STAThread]static int Main(string[] args)
    {
        try{
            string root=args[0], output=args[1];bool unavailable=args.Length>2&&args[2]=="unavailable";Directory.CreateDirectory(output);
            var report=XVisionClient.InspectEnvironment(root);
            Check(report.FilesReady&&report.AuthorizationStatus.StartsWith("未验证"),"Runtime files must never be reported as a validated license");
            string fixture=Path.Combine(output,"fixture"),assembly=Path.Combine(fixture,@"Development\V4.x\ComControls\Assembly"),server=Path.Combine(fixture,@"Applications\Server");
            Directory.CreateDirectory(assembly);Directory.CreateDirectory(server);File.WriteAllText(Path.Combine(assembly,"VM.Core.dll"),"");File.WriteAllText(Path.Combine(server,"ServerAuthConfig.ini"),"AuthorizeType = 2");
            var soft=VmRuntimeDiagnostics.Inspect(fixture);Check(soft.ConfiguredAuthorizationMode==2&&!soft.FilesReady&&soft.AuthorizationStatus.StartsWith("未验证"),"Soft mode selection alone must not be reported as activated");
            int code=unchecked((int)0xE0000700);var error=new VisionSdkException(code,"Load");Check(error.ErrorCode==code&&error.Message.Contains("官方软授权"),"Preserve SDK error code and explain the official software license route");
            Check(VmAuthorizationErrors.GetGuidance(unchecked((int)0xE000070A)).Contains("驱动"),"Missing driver must have distinct guidance");
            Console.WriteLine("PASS read-only diagnostics / soft selector is not activation / actionable errors preserve original codes");
            using(var client=XVisionClient.Create(new XVisionSdkOptions{VisionMasterDirectory=root,ParameterDirectory=Path.Combine(output,"parameters"),BaseDirectory=output})){
                bool cancelled=false;try{client.LoadAsync("cancelled.sol",token:new CancellationToken(true)).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancelled load must not reach native SDK");
                string solution=Path.Combine(root,@"Development\V4.x\Samples\C#\PlatformSDKSampleCS\CircleFind\CircleFind.sol");string copy=Path.Combine(output,"CircleFind.sol");File.Copy(solution,copy);
                try{
                    client.LoadAsync("CircleFind.sol").GetAwaiter().GetResult();
                    Check(!unavailable,"A valid license was unexpectedly available; rerun without the unavailable expectation");
                    string image=Path.Combine(root,@"Development\V4.x\Samples\VC\PlatformSDKSampleVC\GetResultControl\image.jpg");
                    var result=client.InspectAsync(RawImage.FromFile(image)).GetAwaiter().GetResult();Check(result.Modules.Count>0,"SDK run must return real module results");
                    client.SaveAsync().GetAwaiter().GetResult();
                    var running=client.InspectAsync(RawImage.FromFile(image));var unloading=client.UnloadAsync();
                    System.Threading.Tasks.Task.WhenAll(running,unloading).GetAwaiter().GetResult();
                    Check(running.Result.Modules.Count>0&&!client.IsLoaded,"Concurrent unload must wait for the returned inspection result");
                    Console.WriteLine("PASS real SDK load / image input / run / outputs");
                }catch(VisionSdkException ex){
                    if(!unavailable||!VmAuthorizationErrors.IsAuthorizationError(ex.ErrorCode))throw;
                    File.WriteAllText(Path.Combine(output,"authorization-blocked.txt"),ex.Message);
                    Console.WriteLine("EXPECTED BLOCK: native VM authorization error 0x"+unchecked((uint)ex.ErrorCode).ToString("X8")+"; .sol execution is NOT accepted as passed.");
                }
            }
            Check(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"XVision.exe")),"Standalone SDK consumer must not depend on the GUI executable");
            Console.WriteLine("PASS independent SDK consumer without GUI executable / orderly disposal");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
