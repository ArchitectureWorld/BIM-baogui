using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hifc.Web.Revit
{
    // Owned modeless windows let Revit process ExternalEvent while an async
    // operation is pending. The parent session remains busy until this closes.
    internal class NativeDialogWindow : Window
    {
        private TaskCompletionSource<bool> completion;
        private bool accepted;
        internal NativeDialogWindow()
        {
            AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) => { if (e.Source is Button button && button.IsCancel) Close(); }));
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        }
        internal Task<bool> ShowAsync()
        {
            if (completion != null) throw new InvalidOperationException("窗口已打开。");
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); Show(); return completion.Task;
        }
        protected void Complete() { accepted = true; Close(); }
        protected override void OnClosed(EventArgs e) { base.OnClosed(e); completion?.TrySetResult(accepted); }
    }
}
