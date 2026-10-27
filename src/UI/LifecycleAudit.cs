using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Luma.Core;

namespace Luma.UI;

internal static class LifecycleAudit
{
    internal static async Task Run(Controller controller,MainWindow main,string folder,bool explicitExit)
    {
        Directory.CreateDirectory(folder);var results=new List<object>();var failed=0;
        void Check(string name,bool passed){if(!passed)failed++;results.Add(new{name,passed});}
        void Write()=>File.WriteAllText(Path.Combine(folder,"lifecycle-test-results.json"),JsonSerializer.Serialize(new{at=DateTimeOffset.Now,passed=results.Count-failed,failed,tests=results},Store.Json));
        controller.Store.Settings.CloseToTray=true;main.Show();main.Close();
        await main.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Check("Close to tray keeps process and hides main window",!main.IsVisible);
        controller.ShowMain();await Task.Delay(30);Check("Background application can reopen",main.IsVisible);
        Application.Current.Exit+=(_,_)=>{Check(explicitExit?"Explicit exit ignores background preference":"Close with exit preference shuts down process",true);Write();};
        Write();if(explicitExit)main.ExitButtonForAudit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));else{controller.Store.Settings.CloseToTray=false;main.Close();}
    }
}
