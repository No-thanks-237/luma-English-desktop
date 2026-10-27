using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Luma.Services;

internal sealed record UpdateInfo(Version Version,string Notes,string FileName,Uri DownloadUrl,long Size,string Sha256);

internal sealed class UpdateService : IDisposable
{
    internal const string Repository="No-thanks-237/luma-English-desktop";
    internal static Version CurrentVersion {get;}=Normalize(Assembly.GetExecutingAssembly().GetName().Version!);
    private readonly HttpClient http;
    private readonly string root;
    internal UpdateService(string dataRoot,HttpClient? client=null)
    {
        root=Path.Combine(dataRoot,"Updates");http=client??new HttpClient();http.Timeout=TimeSpan.FromMinutes(15);http.MaxResponseContentBufferSize=2*1024*1024;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Luma/"+CurrentVersion);http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    private static Version Normalize(Version v)=>new(v.Major,v.Minor,Math.Max(0,v.Build));
    internal static UpdateInfo? ParseRelease(string json,Version current)
    {
        using var document=JsonDocument.Parse(json);var release=document.RootElement;
        if(release.GetProperty("draft").GetBoolean()||release.GetProperty("prerelease").GetBoolean())return null;
        var tag=release.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"^v?\d+\.\d+\.\d+$")||!Version.TryParse(tag.TrimStart('v'),out var version))throw new InvalidOperationException("新版版本号格式无效。");
        version=Normalize(version);if(version<=Normalize(current))return null;
        var fileName="Luma-"+version+"-Setup-x64.exe";
        foreach(var asset in release.GetProperty("assets").EnumerateArray())
        {
            if(asset.GetProperty("name").GetString()!=fileName)continue;
            var expected="https://github.com/"+Repository+"/releases/download/"+tag+"/"+fileName;
            if(asset.GetProperty("browser_download_url").GetString()!=expected)throw new InvalidOperationException("新版安装包来源不匹配。");
            var digest=asset.TryGetProperty("digest",out var hash)?hash.GetString()??"":"";
            if(!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))throw new InvalidOperationException("新版尚未提供完整的 SHA-256 校验，请稍后检查。");
            var size=asset.GetProperty("size").GetInt64();if(size<=0||size>512L*1024*1024)throw new InvalidOperationException("新版安装包大小异常。");
            var notes=release.TryGetProperty("body",out var body)?body.GetString()??"":"";
            return new(version,notes.Length>5000?notes[..5000]:notes,fileName,new Uri(expected),size,digest[7..].ToLowerInvariant());
        }
        throw new InvalidOperationException("新版正在发布，Windows 安装包尚未准备好，请稍后重试。");
    }
    internal async Task<UpdateInfo?> Check(CancellationToken ct)
    {
        using var response=await http.GetAsync("https://api.github.com/repos/"+Repository+"/releases/latest",ct);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)throw new InvalidOperationException("GitHub 检查次数过多，请稍后再试。");
        response.EnsureSuccessStatusCode();return ParseRelease(await response.Content.ReadAsStringAsync(ct),CurrentVersion);
    }
    internal async Task<string> Download(UpdateInfo info,IProgress<double>? progress,CancellationToken ct)
    {
        var folder=Path.Combine(root,info.Version.ToString());Directory.CreateDirectory(folder);var destination=Path.Combine(folder,info.FileName);var partial=destination+".download";
        if(File.Exists(destination)&&new FileInfo(destination).Length==info.Size)
        {
            await using var cached=File.OpenRead(destination);var hash=await SHA256.HashDataAsync(cached,ct);
            if(Convert.ToHexString(hash).Equals(info.Sha256,StringComparison.OrdinalIgnoreCase)){progress?.Report(1);return destination;}
        }
        try
        {
            using var response=await http.GetAsync(info.DownloadUrl,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
            await using(var source=await response.Content.ReadAsStreamAsync(ct))
            await using(var file=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,81920,true))
            using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer=new byte[81920];long total=0;int count;
                while((count=await source.ReadAsync(buffer,ct))!=0)
                {
                    total+=count;if(total>info.Size)throw new InvalidOperationException("安装包大小与发布记录不一致。");
                    hash.AppendData(buffer,0,count);await file.WriteAsync(buffer.AsMemory(0,count),ct);progress?.Report((double)total/info.Size);
                }
                if(total!=info.Size||!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(),Convert.FromHexString(info.Sha256)))throw new InvalidOperationException("安装包校验失败，已阻止安装。请重新下载。");
            }
            File.Move(partial,destination,true);return destination;
        }
        finally{if(File.Exists(partial))File.Delete(partial);}
    }
    internal static bool HasRuntime()
    {
        using var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
        if(key==null||Convert.ToInt32(key.GetValue("Installed",0))!=1)return false;var major=Convert.ToInt32(key.GetValue("Major",0));return major>14||(major==14&&Convert.ToInt32(key.GetValue("Minor",0))>=29);
    }
    internal static string BuildInstallerScript(string path,UpdateInfo info)
    {
        static string Quote(string s)=>"'"+s.Replace("'","''")+"'";
        var arguments=HasRuntime()?"/LUMAUPDATE=1 /SILENT /SUPPRESSMSGBOXES /NORESTART":"/LUMAUPDATE=1 /NORESTART";
        // Run the waiter from Windows, so no files in the installed app stay locked during replacement.
        var script=string.Join("\n",new[]{
            "$ErrorActionPreference='Stop'", "try {",
            "$parent=$null;try{$parent=[System.Diagnostics.Process]::GetProcessById("+Environment.ProcessId+")}catch [System.ArgumentException]{}",
            "if($parent -and -not $parent.WaitForExit(60000)){throw 'Timeout'}",
            "$sha=[System.Security.Cryptography.SHA256]::Create();$stream=[System.IO.File]::OpenRead("+Quote(path)+")",
            "try{$actual=[System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}",
            "if($actual -ne "+Quote(info.Sha256)+"){throw 'Hash mismatch'}",
            "$start=[System.Diagnostics.ProcessStartInfo]::new();$start.FileName="+Quote(path)+";$start.Arguments="+Quote(arguments)+";$start.UseShellExecute=$true",
            "$setup=[System.Diagnostics.Process]::Start($start);$setup.WaitForExit();if($setup.ExitCode -notin @(0,3010)){throw 'Installer did not complete'}",
            "}catch{[System.IO.File]::WriteAllText("+Quote(path+".update-error.txt")+",$_.Exception.Message);[void][System.Diagnostics.Process]::Start("+Quote(Environment.ProcessPath!)+",'--update-failed')}"
        });
        return script;
    }
    internal static void StartInstaller(string path,UpdateInfo info)
    {
        var script=BuildInstallerScript(path,info);
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        foreach(var argument in new[]{"-NoProfile","-NonInteractive","-WindowStyle","Hidden","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})start.ArgumentList.Add(argument);
        _=Process.Start(start)??throw new InvalidOperationException("无法启动更新程序。");
    }
    public void Dispose()=>http.Dispose();
}
