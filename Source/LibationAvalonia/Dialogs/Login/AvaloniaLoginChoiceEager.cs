using AudibleApi;
using AudibleUtilities;
using Avalonia.Controls;
using Avalonia.Threading;
using LibationFileManager;
using LibationUiBase.Forms;
using System;
using System.Threading.Tasks;

#nullable enable
namespace LibationAvalonia.Dialogs.Login
{
	public class AvaloniaLoginChoiceEager : ILoginChoiceEager
	{
		public ILoginCallback LoginCallback { get; } = new AvaloniaLoginCallback();

		private readonly Account _account;

		public AvaloniaLoginChoiceEager(Account account)
		{
			_account = Dinah.Core.ArgumentValidator.EnsureNotNull(account, nameof(account));
		}

		public async Task<ChoiceOut?> StartAsync(ChoiceIn choiceIn)
			=> await Dispatcher.UIThread.InvokeAsync(() => StartAsyncInternal(choiceIn));

		private async Task<ChoiceOut?> StartAsyncInternal(ChoiceIn choiceIn)
		{
			try
			{
				if (await BrowserLoginAsync(choiceIn.LoginUrl) is ChoiceOut external)
					return external;
			}
			catch (Exception ex)
			{
				Serilog.Log.Logger.Error(ex, $"Failed to use the {nameof(NativeWebDialog)}");
			}

			var externalDialog = new LoginExternalDialog(_account, choiceIn.LoginUrl);
			return await externalDialog.ShowDialogAsync() is DialogResult.OK
				? ChoiceOut.External(externalDialog.ResponseUrl)
				: null;
		}

		private async Task<ChoiceOut?> BrowserLoginAsync(string url)
		{
			TaskCompletionSource<ChoiceOut?> tcs = new();

			NativeWebDialog dialog = new()
			{
				Title = "Audible Login",
				CanUserResize = true,
				Source = new Uri(url)
			};

			dialog.Closing += (_, _) => tcs.TrySetResult(null);
			dialog.AdapterCreated += (_, _) =>
			{
				if (dialog.TryGetWindow() is Window window)
				{
					window.Width = 450;
					window.Height = 700;
				}
			};
			dialog.NavigationCompleted += async (s, e) =>
			{
				if (!e.IsSuccess)
					return;
				if (e.Request?.AbsolutePath.StartsWith("/ap/maplanding") is true)
				{
					tcs.TrySetResult(ChoiceOut.External(e.Request.ToString()));
					dialog.Close();
				}
				else
				{
					await dialog.InvokeScript(getScript(_account.AccountId));
				}
			};

			if (!Configuration.IsLinux && App.MainWindow is TopLevel topLevel)
				dialog.Show(topLevel);
			else
				dialog.Show();

			return await tcs.Task;
		}

		private void Dialog_AdapterCreated(object? sender, WebViewAdapterEventArgs e)
		{
			throw new NotImplementedException();
		}

		private static string getScript(string accountID) => $$"""
			(function() {
				function populateForm(){
					var email = document.querySelector("input[name='email']");
					if (email !== null)
						email.value = '{{accountID}}';
					
					var pass = document.querySelector("input[name='password']");
					if (pass !== null)
						pass.focus();
				}
				window.addEventListener("load", (event) => { populateForm(); });
				populateForm();
			})()
			""";
	}
}
