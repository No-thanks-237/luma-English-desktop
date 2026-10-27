using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Luma.Services;

namespace Luma.UI;

internal sealed class UpdateView : UserControl
{
    private readonly Controller controller;
    private readonly MainWindow owner;
    private readonly Button check,install,cancel;
    private readonly TextBlock status=Theme.T("从 GitHub 正式发布版本检查，保留你的本地数据。",12,Theme.Muted);
    private readonly TextBlock notes=Theme.T("",12,Theme.Muted);
    private readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=4,Margin=new Thickness(0,12,0,0),Visibility=Visibility.Collapsed};
    private UpdateInfo? available;
    private CancellationTokenSource? request;
    internal UpdateView(Controller controller,MainWindow owner)
    {
        this.controller=controller;this.owner=owner;
        var body=new StackPanel();body.Children.Add(Theme.T("应用更新 · 当前 "+UpdateService.CurrentVersion,14,Theme.Ink,true));Theme.Space(body,8);body.Children.Add(status);Theme.Space(body,14);
        var actions=new WrapPanel();check=Theme.Button("检查更新",()=>_=Check());install=Theme.Button("下载并更新",()=>_=Install(),true);install.Margin=new Thickness(8,0,0,0);install.Visibility=Visibility.Collapsed;cancel=Theme.Button("取消下载",()=>request?.Cancel());cancel.Margin=new Thickness(8,0,0,0);cancel.Visibility=Visibility.Collapsed;actions.Children.Add(check);actions.Children.Add(install);actions.Children.Add(cancel);body.Children.Add(actions);body.Children.Add(progress);notes.Margin=new Thickness(0,12,0,0);notes.Visibility=Visibility.Collapsed;body.Children.Add(notes);Content=Theme.Card(body,new Thickness(18));
        owner.Closed+=(_,_)=>request?.Cancel();
    }
    private async Task Check()
    {
        if(request!=null)return;using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(25));request=ct;check.IsEnabled=false;install.Visibility=Visibility.Collapsed;notes.Visibility=Visibility.Collapsed;status.Text="正在检查 GitHub 正式版本…";
        try
        {
            using var service=new UpdateService(controller.Store.Root);available=await service.Check(ct.Token);
            status.Text=available==null?"已是最新版本 "+UpdateService.CurrentVersion:"发现新版 "+available.Version+"，更新会保留设置和单词本。";
            if(available!=null){install.Visibility=Visibility.Visible;notes.Text=available.Notes;notes.Visibility=available.Notes.Length>0?Visibility.Visible:Visibility.Collapsed;}
        }
        catch(OperationCanceledException){status.Text="检查超时，请确认能访问 GitHub 后重试。";}
        catch(Exception ex){status.Text="检查失败："+ex.Message;}
        finally{request=null;check.IsEnabled=true;}
    }
    private async Task Install()
    {
        if(request!=null||available==null)return;
        if(owner.TranslationBusy){status.Text="请先等待或取消当前翻译，再安装更新。";return;}
        using var ct=new CancellationTokenSource(TimeSpan.FromMinutes(15));request=ct;check.IsEnabled=false;install.IsEnabled=false;cancel.Visibility=Visibility.Visible;progress.Visibility=Visibility.Visible;
        try
        {
            status.Text="正在下载并校验新版安装包…";using var service=new UpdateService(controller.Store.Root);
            var path=await service.Download(available,new Progress<double>(p=>{progress.Value=p*100;status.Text="正在下载 "+available.Version+" · "+(int)(p*100)+"%";}),ct.Token);
            if(owner.TranslationBusy){status.Text="安装包已校验，请结束翻译后再次点击更新。";return;}
            status.Text="校验完成，正在保存数据并启动更新…";UpdateService.StartInstaller(path,available);controller.Quit();
        }
        catch(OperationCanceledException){status.Text="下载已取消，可以稍后重试。";}
        catch(Exception ex){status.Text="更新失败："+ex.Message;}
        finally{request=null;check.IsEnabled=true;install.IsEnabled=true;cancel.Visibility=Visibility.Collapsed;progress.Visibility=Visibility.Collapsed;}
    }
}
