﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using FileManager;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

#nullable enable
namespace LibationFileManager
{
	public partial class Configuration
	{
		// note: any potential file manager static ctors can't compensate if storage dir is changed at run time via settings. this is partly bad architecture. but the side effect is desirable. if changing LibationFiles location: restart app

		// default setting and directory creation occur in class responsible for files.
		// config class is only responsible for path. not responsible for setting defaults, dir validation, or dir creation
		// exceptions: appsettings.json, LibationFiles dir, Settings.json

		private PersistentDictionary? persistentDictionary;

		private PersistentDictionary Settings
		{
			get
			{
				if (persistentDictionary is null)
					throw new InvalidOperationException($"{nameof(persistentDictionary)} must first be set by accessing {nameof(LibationFiles)} or calling {nameof(SettingsFileIsValid)}");
				return persistentDictionary;
			}
		}

		public bool RemoveProperty(string propertyName) => Settings.RemoveProperty(propertyName);

		[return: NotNullIfNotNull(nameof(defaultValue))]
		public T? GetNonString<T>(T defaultValue, [CallerMemberName] string propertyName = "")
			=> Settings is null ? default : Settings.GetNonString(propertyName, defaultValue);


		[return: NotNullIfNotNull(nameof(defaultValue))]
		public string? GetString(string? defaultValue = null, [CallerMemberName] string propertyName = "")
			=> Settings?.GetString(propertyName, defaultValue);

		public object? GetObject([CallerMemberName] string propertyName = "") => Settings.GetObject(propertyName);

		public void SetNonString(object? newValue, [CallerMemberName] string propertyName = "")
		{
			var existing = getExistingValue(propertyName);
			if (existing?.Equals(newValue) is true) return;

			OnPropertyChanging(propertyName, existing, newValue);
			Settings.SetNonString(propertyName, newValue);
			OnPropertyChanged(propertyName, newValue);
		}

		public void SetString(string? newValue, [CallerMemberName] string propertyName = "")
		{
			var existing = getExistingValue(propertyName);
			if (existing?.Equals(newValue) is true) return;

			OnPropertyChanging(propertyName, existing, newValue);
			Settings.SetString(propertyName, newValue);
			OnPropertyChanged(propertyName, newValue);
		}

		private object? getExistingValue(string propertyName)
		{
			var property = GetType().GetProperty(propertyName);
			if (property is not null) return property.GetValue(this);
			return GetObject(propertyName);
		}

		/// <summary>WILL ONLY set if already present. WILL NOT create new</summary>
		public void SetWithJsonPath(string jsonPath, string propertyName, string? newValue, bool suppressLogging = false)
		{
			var settingWasChanged = Settings.SetWithJsonPath(jsonPath, propertyName, newValue, suppressLogging);
			if (settingWasChanged)
				configuration?.Reload();
		}

		public string SettingsFilePath => Path.Combine(LibationFiles, "Settings.json");

		public static string GetDescription(string propertyName)
		{
			var attribute = typeof(Configuration)
				.GetProperty(propertyName)
				?.GetCustomAttributes(typeof(DescriptionAttribute), true)
				.SingleOrDefault()
				as DescriptionAttribute;

			return attribute?.Description ?? $"[{propertyName}]";
		}

		public bool Exists(string propertyName) => Settings.Exists(propertyName);

		[Description("Set cover art as the folder's icon.")]
		public bool UseCoverAsFolderIcon { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Save audiobook metadata to metadata.json")]
		public bool SaveMetadataToFile { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Book display grid size")]
		public float GridScaleFactor { get => float.Min(2, float.Max(0.5f, GetNonString(defaultValue: 1f))); set => SetNonString(value); }

		[Description("Book display font size")]
		public float GridFontScaleFactor { get => float.Min(2, float.Max(0.5f, GetNonString(defaultValue: 1f))); set => SetNonString(value); }

		[Description("Use the beta version of Libation\r\nNew and experimental features, but probably buggy.\r\n(requires restart to take effect)")]
		public bool BetaOptIn { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Location for book storage. Includes destination of newly liberated books")]
		public LongPath? Books {
			get => GetString();
			set
			{
				if (value != Books)
				{
					OnPropertyChanging(nameof(Books), Books, value);
					Settings.SetString(nameof(Books), value);
					m_BooksCanWrite255UnicodeChars = null;
					m_BooksCanWriteWindowsInvalidChars = null;
					OnPropertyChanged(nameof(Books), value);
				}
			}
		}

		private bool? m_BooksCanWrite255UnicodeChars;
		private bool? m_BooksCanWriteWindowsInvalidChars;
		/// <summary>
		/// True if the Books directory can be written to with 255 unicode character filenames
		/// <para/> Does not persist. Check and set this value at runtime and whenever Books is changed.
		/// </summary>
		public bool BooksCanWrite255UnicodeChars => m_BooksCanWrite255UnicodeChars ??= FileSystemTest.CanWrite255UnicodeChars(AudibleFileStorage.BooksDirectory);
		/// <summary>
		/// True if the Books directory can be written to with filenames containing characters invalid on Windows (:, *, ?, &lt;, &gt;, |)
		/// <para/> Always false on Windows platforms.
		/// <para/> Does not persist. Check and set this value at runtime and whenever Books is changed.
		/// </summary>
		public bool BooksCanWriteWindowsInvalidChars => !IsWindows && (m_BooksCanWriteWindowsInvalidChars ??= FileSystemTest.CanWriteWindowsInvalidChars(AudibleFileStorage.BooksDirectory));

		[Description("Overwrite existing files if they already exist?")]
		public bool OverwriteExisting { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		// temp/working dir(s) should be outside of dropbox
		[Description("Temporary location of files while they're in process of being downloaded and decrypted.\r\nWhen decryption is complete, the final file will be in Books location\r\nRecommend not using a folder which is backed up real time. Eg: Dropbox, iCloud, Google Drive")]
		public string InProgress
		{
			get
			{
				var tempDir = GetString();
				return string.IsNullOrWhiteSpace(tempDir) ? WinTemp : tempDir;
			}
			set => SetString(value);
		}

		[Description("Allow Libation to fix up audiobook metadata")]
		public bool AllowLibationFixup { get => GetNonString(defaultValue: true); set => SetNonString(value); }

		[Description("Create a cue sheet (.cue)")]
		public bool CreateCueSheet { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Retain the Aax file after successfully decrypting")]
		public bool RetainAaxFile { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Split my books into multiple files by chapter")]
		public bool SplitFilesByChapter { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Merge Opening/End Credits into the following/preceding chapters")]
		public bool MergeOpeningAndEndCredits { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Strip \"(Unabridged)\" from audiobook metadata tags")]
		public bool StripUnabridged { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Strip audible branding from the start and end of audiobooks.\r\n(e.g. \"This is Audible\")")]
		public bool StripAudibleBrandAudio { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Decrypt to lossy format?")]
		public bool DecryptToLossy { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Move the mp4 moov atom to the beginning of the file?")]
		public bool MoveMoovToBeginning { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Lame encoder target. true = Bitrate, false = Quality")]
		public bool LameTargetBitrate { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Maximum audio sample rate")]
		public AAXClean.SampleRate MaxSampleRate { get => GetNonString(defaultValue: AAXClean.SampleRate.Hz_44100); set => SetNonString(value); }

		[Description("Lame encoder quality")]
		public NAudio.Lame.EncoderQuality LameEncoderQuality { get => GetNonString(defaultValue: NAudio.Lame.EncoderQuality.High); set => SetNonString(value); }

		[Description("Lame encoder downsamples to mono")]
		public bool LameDownsampleMono { get => GetNonString(defaultValue: true); set => SetNonString(value); }

		[Description("Lame target bitrate [16,320]")]
		public int LameBitrate { get => GetNonString(defaultValue: 64); set => SetNonString(value); }

		[Description("Restrict encoder to constant bitrate?")]
		public bool LameConstantBitrate { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Match the source bitrate?")]
		public bool LameMatchSourceBR { get => GetNonString(defaultValue: true); set => SetNonString(value); }

		[Description("Lame target VBR quality [10,100]")]
		public int LameVBRQuality { get => GetNonString(defaultValue: 2); set => SetNonString(value); }

		private static readonly EquatableDictionary<string, bool> DefaultColumns = new([
			new ("SeriesOrder", false),
			new ("LastDownload", false),
			new ("IsSpatial", false),
			new ("IncludedUntil", false), 
			]);
		public bool GetColumnVisibility(string columnName)
			=> GridColumnsVisibilities.TryGetValue(columnName, out var isVisible) ? isVisible
			:DefaultColumns.GetValueOrDefault(columnName, true);

		[Description("A Dictionary of GridView data property names and bool indicating its column's visibility in ProductsGrid")]
		public Dictionary<string, bool> GridColumnsVisibilities { get => GetNonString(defaultValue: DefaultColumns).Clone(); set => SetNonString(value); }

		[Description("A Dictionary of GridView data property names and int indicating its column's display index in ProductsGrid")]
		public Dictionary<string, int> GridColumnsDisplayIndices { get => GetNonString(defaultValue: new EquatableDictionary<string, int>()).Clone(); set => SetNonString(value); }

		[Description("A Dictionary of GridView data property names and int indicating its column's width in ProductsGrid")]
		public Dictionary<string, int> GridColumnsWidths { get => GetNonString(defaultValue: new EquatableDictionary<string, int>()).Clone(); set => SetNonString(value); }

		[Description("Save cover image alongside audiobook?")]
		public bool DownloadCoverArt { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Combine nested chapter titles")]
		public bool CombineNestedChapterTitles { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Download clips and bookmarks?")]
		public bool DownloadClipsBookmarks { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("File format to save clips and bookmarks")]
		public ClipBookmarkFormat ClipsBookmarksFileFormat { get => GetNonString(defaultValue: ClipBookmarkFormat.CSV); set => SetNonString(value); }

		[JsonConverter(typeof(StringEnumConverter))]
		public enum ClipBookmarkFormat
		{
			[Description("Comma-separated values")]
			CSV,
			[Description("Microsoft Excel Spreadsheet")]
			Xlsx,
			[Description("JavaScript Object Notation (JSON)")]
			Json
		}

		[JsonConverter(typeof(StringEnumConverter))]
		public enum BadBookAction
		{
			[Description("Ask each time what action to take.")]
			Ask = 0,
			[Description("Stop processing books.")]
			Abort = 1,
			[Description("Retry book later. Skip for now. Continue processing books.")]
			Retry = 2,
			[Description("Permanently ignore book. Continue processing books. Do not try book again.")]
			Ignore = 3
		}

		[JsonConverter(typeof(StringEnumConverter))]
		public enum DateTimeSource
		{
			[Description("File creation date/time")]
			File,
			[Description("Audiobook publication date")]
			Published,
			[Description("Date book was added to your Audible account")]
			Added
		}

		[JsonConverter(typeof(StringEnumConverter))]
		public enum DownloadQuality
		{
			High,
			Normal
		}

		[JsonConverter(typeof(StringEnumConverter))]
		public enum SpatialCodec
		{
			EC_3,
			AC_4
		}

		[Description("Use Widevine DRM")]
		public bool UseWidevine { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Request xHE-AAC codec")]
		public bool Request_xHE_AAC { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Request Spatial Audio")]
		public bool RequestSpatial { get => GetNonString(defaultValue: true); set => SetNonString(value); }

		[Description("Spatial audio codec:")]
		public SpatialCodec SpatialAudioCodec { get => GetNonString(defaultValue: SpatialCodec.EC_3); set => SetNonString(value); }

		[Description("Audio quality to request from Audible:")]
		public DownloadQuality FileDownloadQuality { get => GetNonString(defaultValue: DownloadQuality.High); set => SetNonString(value); }

		[Description("Set file \"created\" timestamp to:")]
		public DateTimeSource CreationTime { get => GetNonString(defaultValue: DateTimeSource.File); set => SetNonString(value); }

		[Description("Set file \"modified\" timestamp to:")]
		public DateTimeSource LastWriteTime { get => GetNonString(defaultValue: DateTimeSource.File); set => SetNonString(value); }

		[Description("Indicates that this is the first time Libation has been run")]
		public bool FirstLaunch { get => GetNonString(defaultValue: true); set => SetNonString(value); }

		[Description("When liberating books and there is an error, Libation should:")]
		public BadBookAction BadBook { get => GetNonString(defaultValue: BadBookAction.Ask); set => SetNonString(value); }

		[Description("Show number of newly imported titles? When unchecked, no pop-up will appear after library scan.")]
		public bool ShowImportedStats { get => GetNonString(defaultValue: true); set => SetNonString(value); }

		[Description("Import episodes? (eg: podcasts) When unchecked, episodes will not be imported into Libation.")]
		public bool ImportEpisodes { get => GetNonString(defaultValue: true); set => SetNonString(value); }

		[Description("Download episodes? (eg: podcasts). When unchecked, episodes already in Libation will not be downloaded.")]
		public bool DownloadEpisodes { get => GetNonString(defaultValue: true); set => SetNonString(value); }

		[Description("Automatically run periodic scans in the background?")]
		public bool AutoScan { get => GetNonString(defaultValue: true); set => SetNonString(value); }

		[Description("Auto download books? After scan, download new books in 'checked' accounts.")]
		// poorly named setting. Should just be 'AutoDownload'. It is NOT episode specific
		public bool AutoDownloadEpisodes { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Save all podcast episodes in a series to the series parent folder?")]
		public bool SavePodcastsToParentFolder { get => GetNonString(defaultValue: false); set => SetNonString(value); }

		[Description("Global download speed limit in bytes per second.")]
		public long DownloadSpeedLimit
		{
			get
			{
				var limit = GetNonString(defaultValue: 0L);
				return limit <= 0 ? 0 : Math.Max(limit, AaxDecrypter.NetworkFileStream.MIN_BYTES_PER_SECOND);
			}
			set
			{
				var limit = value <= 0 ? 0 : Math.Max(value, AaxDecrypter.NetworkFileStream.MIN_BYTES_PER_SECOND);
				SetNonString(limit);
			}
		}

		#region templates: custom file naming

		[Description("Edit how filename characters are replaced")]
		public ReplacementCharacters ReplacementCharacters { get => GetNonString(defaultValue: ReplacementCharacters.Default(IsWindows)); set => SetNonString(value); }

		[Description("How to format the folders in which files will be saved")]
		public string FolderTemplate
		{
			get => getTemplate<Templates.Templates.FolderTemplate>();
			set => setTemplate<Templates.Templates.FolderTemplate>(value);
		}

		[Description("How to format the saved pdf and audio files")]
		public string FileTemplate
		{
			get => getTemplate<Templates.Templates.FileTemplate>();
			set => setTemplate<Templates.Templates.FileTemplate>(value);
		}

		[Description("How to format the saved audio files when split by chapters")]
		public string ChapterFileTemplate
		{
			get => getTemplate<Templates.Templates.ChapterFileTemplate>();
			set => setTemplate<Templates.Templates.ChapterFileTemplate>(value);
		}

		[Description("How to format the file's Title stored in metadata")]
		public string ChapterTitleTemplate
		{
			get => getTemplate<Templates.Templates.ChapterTitleTemplate>();
			set => setTemplate<Templates.Templates.ChapterTitleTemplate>(value);
		}

		private string getTemplate<T>([CallerMemberName] string propertyName = "")
			where T : Templates.Templates, Templates.ITemplate, new()
		{
			return Templates.Templates.GetTemplate<T>(GetString(defaultValue: T.DefaultTemplate, propertyName)).TemplateText;
		}

		private void setTemplate<T>(string newValue, [CallerMemberName] string propertyName = "")
			where T : Templates.Templates, Templates.ITemplate, new()
		{
			SetString(Templates.Templates.GetTemplate<T>(newValue).TemplateText, propertyName);
		}
		#endregion
	}
}
