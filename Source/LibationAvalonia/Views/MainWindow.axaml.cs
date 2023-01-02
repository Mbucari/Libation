using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ApplicationServices;
using AppScaffolding;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using DataLayer;
using Dinah.Core;
using LibationAvalonia.Dialogs;
using LibationAvalonia.ViewModels;
using LibationFileManager;

namespace LibationAvalonia.Views
{
	public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
	{
		public event EventHandler Load;
		public event EventHandler<List<LibraryBook>> LibraryLoaded;
		private readonly MainWindowViewModel _viewModel;

		public MainWindow()
		{
			this.DataContext = _viewModel = new MainWindowViewModel();

			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif
			this.FindAllControls();


			// eg: if one of these init'd productsGrid, then another can't reliably subscribe to it
			Configure_BackupCounts();
			Configure_ScanAuto();
			Configure_ScanNotification();
			Configure_VisibleBooks();
			Configure_QuickFilters();
			Configure_ScanManual();
			Configure_RemoveBooks();
			Configure_Liberate();
			Configure_Export();
			Configure_Settings();
			Configure_ProcessQueue();
			Configure_Filter();
			// misc which belongs in winforms app but doesn't have a UI element
			Configure_NonUI();

			_viewModel.ProductsDisplay.RemovableCountChanged += ProductsDisplay_RemovableCountChanged;
			_viewModel.ProductsDisplay.VisibleCountChanged += ProductsDisplay_VisibleCountChanged;

			{
				this.LibraryLoaded += MainWindow_LibraryLoaded;

				LibraryCommands.LibrarySizeChanged += async (_, _) => await _viewModel.ProductsDisplay.DisplayBooksAsync(DbContexts.GetLibrary_Flat_NoTracking(includeParents: true));
				Closing += (_, _) => this.SaveSizeAndLocation(Configuration.Instance);
			}
			Opened += MainWindow_Opened;
			Closing += MainWindow_Closing;
		}

		private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			productsDisplay?.CloseImageDisplay();
		}

		private async void MainWindow_Opened(object sender, EventArgs e)
		{
#if !DEBUG
			//This is temporaty until we have a solution for linux/mac so that
			//Libation doesn't download a zip every time it runs.
			if (!Configuration.IsWindows)
				return;

			try
			{
				(string zipFile, UpgradeProperties upgradeProperties) = await Task.Run(downloadUpdate);

				if (string.IsNullOrEmpty(zipFile) || !System.IO.File.Exists(zipFile))
					return;

				var result = await MessageBox.Show($"{upgradeProperties.HtmlUrl}\r\n\r\nWould you like to upgrade now?", "New version available", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);

				if (result != DialogResult.Yes)
					return;

				if (Configuration.IsWindows)
				{
					runWindowsUpgrader(zipFile);
				}
				else if (Configuration.IsLinux)
				{

				}
				else if (Configuration.IsMacOs)
				{

				}
			}
			catch (Exception ex)
			{
				Serilog.Log.Logger.Error(ex, "An error occured while checking for app updates.");
				return;
			}
#endif
		}

		private async Task<(string zipFile, UpgradeProperties release)> downloadUpdate()
		{
			UpgradeProperties upgradeProperties;
			try
			{
				upgradeProperties = LibationScaffolding.GetLatestRelease();
				if (upgradeProperties is null)
					return (null,null);
			}
			catch (Exception ex)
			{
				Serilog.Log.Logger.Error(ex, "Failed to check for update");
				return (null, null);
			}

			if (upgradeProperties.ZipUrl is null)
			{
				Serilog.Log.Logger.Information("Download link for new version not found");
				return (null, null);
			}

			//Silently download the update in the background, save it to a temp file.

			var zipFile = System.IO.Path.GetTempFileName();
			try
			{
				System.Net.Http.HttpClient cli = new();
				using var fs = System.IO.File.OpenWrite(zipFile);
				using var dlStream = await cli.GetStreamAsync(new Uri(upgradeProperties.ZipUrl));
					await dlStream.CopyToAsync(fs);
			}
			catch (Exception ex)
			{
				Serilog.Log.Logger.Error(ex, "Failed to download the update: {pdate}", upgradeProperties.ZipUrl);
				return (null, null);
			}
			return (zipFile, upgradeProperties);
		}

		private void runWindowsUpgrader(string zipFile)
		{
			var thisExe = Environment.ProcessPath;
			var thisDir = System.IO.Path.GetDirectoryName(thisExe);

			var zipExtractor = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZipExtractor.exe");

			System.IO.File.Copy("ZipExtractor.exe", zipExtractor, overwrite: true);

			var psi = new System.Diagnostics.ProcessStartInfo()
			{
				FileName = zipExtractor,
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
				CreateNoWindow = true,
				ArgumentList =
				{
					"--input",
					zipFile,
					"--output",
					thisDir,
					"--executable",
					thisExe
				}
			};

			System.Diagnostics.Process.Start(psi);
			Environment.Exit(0);
		}

		private async void MainWindow_LibraryLoaded(object sender, List<LibraryBook> dbBooks)
		{
			if (QuickFilters.UseDefault)
				await performFilter(QuickFilters.Filters.FirstOrDefault());

			await _viewModel.ProductsDisplay.DisplayBooksAsync(dbBooks);
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		public void OnLoad() => Load?.Invoke(this, EventArgs.Empty);
		public void OnLibraryLoaded(List<LibraryBook> initialLibrary) => LibraryLoaded?.Invoke(this, initialLibrary);

		private void FindAllControls()
		{
			quickFiltersToolStripMenuItem = this.FindControl<MenuItem>(nameof(quickFiltersToolStripMenuItem));
			productsDisplay = this.FindControl<ProductsDisplay>(nameof(productsDisplay));
		}
	}
}