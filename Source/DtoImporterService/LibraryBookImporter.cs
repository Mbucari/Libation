﻿using System;
using System.Collections.Generic;
using System.Linq;
using AudibleUtilities;
using DataLayer;
using Dinah.Core;
using Dinah.Core.Collections.Generic;

namespace DtoImporterService
{
    public class LibraryBookImporter : ItemsImporterBase
    {
  	  protected override IValidator Validator => new LibraryValidator();

  	  private BookImporter bookImporter { get; }

  	  public LibraryBookImporter(LibationContext context) : base(context)
  	  {
  		  bookImporter = new BookImporter(DbContext);
  	  }

  	  protected override int DoImport(IEnumerable<ImportItem> importItems)
  	  {
  		  bookImporter.Import(importItems);

  		  var qtyNew = upsertLibraryBooks(importItems);
  		  return qtyNew;
  	  }

  	  private int upsertLibraryBooks(IEnumerable<ImportItem> importItems)
  	  {
  		  // technically, we should be able to have duplicate books from separate accounts.
  		  // this would violate the current pk and would be difficult to deal with elsewhere:
  		  // - what to show in the grid
  		  // - which to consider liberated
  		  //
  		  // sqlite cannot alter pk. the work around is an extensive headache
  		  // - update: now possible in .net5/efcore5
  		  //
  		  // currently, inserting LibraryBook will throw error if the same book is in multiple accounts for the same region.
  		  //
  		  // CURRENT SOLUTION: don't re-insert


  		  //When Books are upserted during the BookImporter run, they are linked to their LibraryBook in the DbContext
  		  //instance. If a LibraryBook has a null book here, that means it's Book was not imported during by BookImporter.
  		  //There should never be duplicates, but this is defensive.
  		  var existingEntries = DbContext.LibraryBooks.AsEnumerable().Where(l => l.Book is not null).ToDictionarySafe(l => l.Book.AudibleProductId);

  		  //If importItems are contains duplicates by asin, keep the Item that's "available"
  		  var uniqueImportItems = ToDictionarySafe(importItems, dto => dto.DtoItem.ProductId, tieBreak);

  		  int qtyNew = 0;			

  		  foreach (var item in uniqueImportItems.Values)
  		  {
  			  if (existingEntries.TryGetValue(item.DtoItem.ProductId, out LibraryBook existing))
  			  {
  				  if (existing.Account != item.AccountId)
  				  {
  					  //Book is absent from the existing LibraryBook's account. Use the alternate account.
  					  existing.SetAccount(item.AccountId);
  				  }

  				  existing.AbsentFromLastScan = isUnavailable(item);
  			  }
  			  else
  			  {
  				  existing = new LibraryBook(
  					  bookImporter.Cache[item.DtoItem.ProductId],
  					  item.DtoItem.DateAdded,
  					  item.AccountId)
  					  {
  						  AbsentFromLastScan = isUnavailable(item)
  					  };

  				  try
  				  {
  					  DbContext.LibraryBooks.Add(existing);
  					  qtyNew++;
  				  }
  				  catch (Exception ex)
  				  {
  					  Serilog.Log.Logger.Error(ex, "Error adding library book. {@DebugInfo}", new { existing.Book, existing.Account });
  				  }
  			  }
  			  
  			  existing.SetIncludedUntil(GetExpirationDate(item));
  		  }

  		  var scannedAccounts = importItems.Select(i => i.AccountId).Distinct().ToList();

  		  //If an existing Book wasn't found in the import, the owning LibraryBook's Book will be null.
  		  //Only change AbsentFromLastScan for LibraryBooks of accounts that were scanned.
  		  foreach (var nullBook in DbContext.LibraryBooks.AsEnumerable().Where(lb => lb.Book is null && lb.Account.In(scannedAccounts)))
  			  nullBook.AbsentFromLastScan = true;

  		  return qtyNew;
  	  }		

  	  private static Dictionary<TKey, TSource> ToDictionarySafe<TKey, TSource>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TSource, TSource> tieBreaker)
  	  {
  		  var dictionary = new Dictionary<TKey, TSource>();

  		  foreach (TSource newItem in source)
  		  {
  			  TKey key = keySelector(newItem);

  			  dictionary[key]
  				  = dictionary.TryGetValue(key, out TSource existingItem)
  				  ? tieBreaker(existingItem, newItem)
  				  : newItem;
  		  }
  		  return dictionary;
  	  }

  	  private static ImportItem tieBreak(ImportItem item1, ImportItem item2)
  		  => isUnavailable(item1) && !isUnavailable(item2) ? item2 : item1;

  	  private static bool isUnavailable(ImportItem item)
  		  => isFutureRelease(item) || isPlusTitleUnavailable(item);

        private static bool isFutureRelease(ImportItem item)
  		  => item.DtoItem.IssueDate is DateTimeOffset dt && dt > DateTimeOffset.UtcNow;

  	  private static bool isPlusTitleUnavailable(ImportItem item)
  		  => item.DtoItem.ContentType is null
  		  || (item.DtoItem.IsAyce is true && item.DtoItem.Plans?.Any(p => p.IsAyce) is not true);

  	  /// <summary>
  	  /// Determines when your audible plus or free book will expire from your library  
  	  /// plan.IsAyce from underlying AudibleApi project determines the plans to look at, first plan found is used.
  	  /// In some cases current date is later than end date so exclude.
  	  /// </summary>
  	  /// <returns>The DateTime that this title will become unavailable, otherwise null</returns>
  	  private static DateTime? GetExpirationDate(ImportItem item)
  		  => item.DtoItem.Plans
  		  ?.Where(p => p.IsAyce)
  		  .Select(p => p.EndDate)
  		  .FirstOrDefault(end => end.HasValue && end.Value.Year is not (2099 or 9999) && end.Value.LocalDateTime >= DateTime.Now) 
  		  ?.DateTime;
    }
}