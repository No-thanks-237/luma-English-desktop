using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Luma.Services;

namespace Luma;

internal static class UpdateTests
{
    internal static void Run(Action<string,Action> check,string folder)
    {
        var payload=Encoding.UTF8.GetBytes("Isolated mock installer bytes.");var digest=Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        string Release(string version="0.1.6",bool prerelease=false,string? url=null,string? hash=null)=>JsonSerializer.Serialize(new{tag_name="v"+version,draft=false,prerelease,body="Release notes",assets=new[]{new{name="Luma-"+version+"-Setup-x64.exe",browser_download_url=url??"https://github.com/"+UpdateService.Repository+"/releases/download/v"+version+"/Luma-"+version+"-Setup-x64.exe",size=payload.Length,digest="sha256:"+(hash??digest)}}});
        void Assert(bool condition){if(!condition)throw new Exception("Update assertion failed");}
        void Rejected(Action action){var rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Assert(rejected);}
        check("Updates: version comparison, prereleases and no downgrade",()=>{
            Assert(UpdateService.ParseRelease(Release(),new Version(0,1,5))?.Version==new Version(0,1,6));Assert(UpdateService.ParseRelease(Release("0.1.5"),new Version(0,1,5))==null);Assert(UpdateService.ParseRelease(Release("0.1.4"),new Version(0,1,5))==null);Assert(UpdateService.ParseRelease(Release(prerelease:true),new Version(0,1,5))==null);
        });
        check("Updates: trusted repository and checksum metadata required",()=>{
            Rejected(()=>UpdateService.ParseRelease(Release(url:"https://example.com/untrusted.exe"),new Version(0,1,5)));Rejected(()=>UpdateService.ParseRelease(Release(hash:""),new Version(0,1,5)));
        });
        check("Updates: verified download and cached installer",()=>{
            var handler=new MockDownload(payload);using var service=new UpdateService(Path.Combine(folder,"update-valid"),new HttpClient(handler));var info=UpdateService.ParseRelease(Release(),new Version(0,1,5))!;
            var path=service.Download(info,null,CancellationToken.None).GetAwaiter().GetResult();Assert(File.ReadAllBytes(path).SequenceEqual(payload));Assert(service.Download(info,null,CancellationToken.None).GetAwaiter().GetResult()==path&&handler.Count==1);
        });
        check("Updates: corrupt download rejected and partial file removed",()=>{
            var root=Path.Combine(folder,"update-corrupt");using var service=new UpdateService(root,new HttpClient(new MockDownload(payload)));var info=UpdateService.ParseRelease(Release(hash:new string('0',64)),new Version(0,1,5))!;
            Rejected(()=>service.Download(info,null,CancellationToken.None).GetAwaiter().GetResult());Assert(!Directory.GetFiles(root,"*",SearchOption.AllDirectories).Any());
        });
        check("Updates: incomplete and cancelled download blocked",()=>{
            var root=Path.Combine(folder,"update-short");using var service=new UpdateService(root,new HttpClient(new MockDownload(new byte[]{1,2})));var info=UpdateService.ParseRelease(Release(),new Version(0,1,5))!;
            Rejected(()=>service.Download(info,null,CancellationToken.None).GetAwaiter().GetResult());Assert(!Directory.GetFiles(root,"*",SearchOption.AllDirectories).Any());
            using var ct=new CancellationTokenSource();ct.Cancel();try{service.Download(info,null,ct.Token).GetAwaiter().GetResult();throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}Assert(!Directory.GetFiles(root,"*",SearchOption.AllDirectories).Any());
        });
    }
    private sealed class MockDownload(byte[] payload):HttpMessageHandler
    {
        internal int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();Count++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)});}
    }
}
