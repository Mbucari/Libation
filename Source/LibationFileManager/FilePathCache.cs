using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileManager;
using Newtonsoft.Json;

#nullable enable
namespace LibationFileManager
{

	public record FileCacheV2<TEntry>
	{
		[JsonProperty]
		private ConcurrentDictionary<string, List<TEntry>> Dictionary = new();

		public List<TEntry> GetIdEntries(string id)
		{
			static List<TEntry> empty() => new();

			return Dictionary.TryGetValue(id, out var entries) ? entries.ToList() : empty();
		}

		public void Add(string id, TEntry entry)
		{
			Dictionary.AddOrUpdate(id, [entry], (id, entries) => { entries.Add(entry); return entries; });
		}

		public void AddRange(string id, IEnumerable<TEntry> entries)
		{
			Dictionary.AddOrUpdate(id, entries.ToList(), (id, entries) =>
			{
				entries.AddRange(entries);
				return entries;
			});
		}

		public bool Remove(string id, TEntry entry)
			=> Dictionary.TryGetValue(id, out List<TEntry>? entries) && (entries?.Remove(entry) ?? false);
	}

	public static class FilePathCache
	{
		public record CacheEntry(string Id, FileType FileType, LongPath Path);

		private const string FILENAME_V2 = "FileLocationsV2.json";
		private const string FILENAME_V1 = "FileLocations.json";

		public static event EventHandler<CacheEntry>? Inserted;
		public static event EventHandler<CacheEntry>? Removed;

		private static LongPath jsonFileV1 => Path.Combine(Configuration.Instance.LibationFiles, FILENAME_V1);
		private static LongPath jsonFileV2 => Path.Combine(Configuration.Instance.LibationFiles, FILENAME_V2);

		private static readonly FileCacheV2<CacheEntry> Cache = new();

		static FilePathCache()
		{
			// load json into memory. if file doesn't exist, nothing to do. save() will create if needed
			if (!File.Exists(jsonFileV2))
			{
				if (File.Exists(jsonFileV1))
				{
					var v1Entries = JsonConvert.DeserializeObject<List<CacheEntry>>(File.ReadAllText(jsonFileV1));
					if (v1Entries == null)
						return;

					//Try to migrate from version 1 file cache.
					foreach (var asin in v1Entries.Select(i => i.Id).Distinct())
					{
						var cacheItems = v1Entries.Where(i => i.Id == asin).ToList();
						Cache.AddRange(asin, cacheItems.Select(i => new CacheEntry(asin, FileTypes.GetFileTypeFromPath(i.Path), i.Path)));
					}
					save();
				}
				
				return;
			}

			try
			{
				Cache = JsonConvert.DeserializeObject<FileCacheV2<CacheEntry>>(File.ReadAllText(jsonFileV2))
					?? throw new NullReferenceException("File exists but deserialize is null. This will never happen when file is healthy.");
			}
			catch (Exception ex)
			{
				Serilog.Log.Logger.Error(ex, "Error deserializing file. Wrong format. Possibly corrupt. Deleting file. {@DebugInfo}", new { jsonFileV2 });
				lock (locker)
					File.Delete(jsonFileV2);
				return;
			}
		}

		public static bool Exists(string id, FileType type) => GetFirstPath(id, type) is not null;

		public static List<(FileType fileType, LongPath path)> GetFiles(string id)
		{
			var matchingFiles = Cache.GetIdEntries(id);

			for (int i = matchingFiles.Count - 1; i >= 0; i--)
			{
				if (!File.Exists(matchingFiles[i].Path))
				{
					matchingFiles.RemoveAt(i);
					Cache.Remove(id, matchingFiles[i]);
				}
			}
			return matchingFiles.Select(e => (e.FileType, e.Path)).ToList();
		}

		public static LongPath? GetFirstPath(string id, FileType type)
		{
			var firstEntry = Cache.GetIdEntries(id).FirstOrDefault(e => e.FileType == type);

			if (firstEntry == null)
				return null;
			else if (!File.Exists(firstEntry.Path))
			{
				Cache.Remove(id, firstEntry);
				return null;
			}
			else
				return firstEntry.Path;
		}

		public static void Insert(string id, string path)
		{
			var type = FileTypes.GetFileTypeFromPath(path);
			Insert(new CacheEntry(id, type, path));
		}

		public static void Insert(CacheEntry entry)
		{
			Cache.Add(entry.Id, entry);
			Inserted?.Invoke(null, entry);
			save();
		}

		// cache is thread-safe and lock free. but file saving is not
		private static object locker { get; } = new object();
		private static void save()
		{
			// create json if not exists
			static void resave() => File.WriteAllText(jsonFileV2, JsonConvert.SerializeObject(Cache, Formatting.Indented));

			lock (locker)
			{
				try { resave(); }
				catch (IOException)
				{
					try { resave(); }
					catch (IOException ex)
					{
						Serilog.Log.Logger.Error(ex, $"Error saving {FILENAME_V2}");
						throw;
					}
				}
			}
		}
	}
}
