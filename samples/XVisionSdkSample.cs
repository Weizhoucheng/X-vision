using System;
using System.IO;
using VisionInspect.Core;
using XVision.Sdk;
internal static class XVisionSdkSample
{
    [STAThread] static int Main(string[] args)
    {
        if(args.Length!=3){Console.WriteLine("Usage: XVisionSdkSample.exe <VM-directory> <solution.sol> <image-file>");return 2;}
        try{
            using(var client=XVisionClient.Create(new XVisionSdkOptions{VisionMasterDirectory=args[0],ParameterDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"parameters")})){
                var info=client.LoadAsync(args[1]).GetAwaiter().GetResult();
                var result=client.InspectFileAsync(args[2]).GetAwaiter().GetResult();
                Console.WriteLine("Procedure: "+info.PrimaryProcedureName+"; result: "+result.JudgeText+"; duration: "+result.DurationMs+" ms");
                foreach(var value in result.Outputs)Console.WriteLine(value.Name+"="+value.JoinedValue);
            }return 0;
        }catch(VisionSdkException error){Console.Error.WriteLine(error.Message);return VmAuthorizationErrors.IsAuthorizationError(error.ErrorCode)?3:1;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
