﻿using System;
using System.Collections.Generic;
using System.Linq;
using AudibleApi.Common;

namespace AudibleUtilities
{
	public interface IValidator
	{
		IEnumerable<Exception> Validate(IEnumerable<Item> items);

		public static IValidator[] GetAllValidators()
			=> new IValidator[]
			{
				new LibraryValidator(),
				new BookValidator(),
				new CategoryValidator(),
				new ContributorValidator(),
				new SeriesValidator(),
			};
	}

	public class LibraryValidator : IValidator
	{
		public IEnumerable<Exception> Validate(IEnumerable<Item> items)
		{
			var exceptions = new List<Exception>();

			if (items.Any(i => string.IsNullOrWhiteSpace(i.ProductId)))
				exceptions.Add(new ArgumentException($"Collection contains item(s) with null or blank {nameof(Item.ProductId)}", nameof(items)));
            //// unfortunately, an actual user has a title with a beginning-of-time 'purchase_date'
            //if (items.Any(i => i.DateAdded < new DateTime(1980, 1, 1)))
            //	exceptions.Add(new ArgumentException($"Collection contains item(s) with invalid {nameof(Item.DateAdded)}", nameof(items)));

            return exceptions;
		}
	}
	public class BookValidator : IValidator
	{
		public IEnumerable<Exception> Validate(IEnumerable<Item> items)
		{
			var exceptions = new List<Exception>();

			// a book having no authors is rare but allowed

			if (items.Any(i => string.IsNullOrWhiteSpace(i.ProductId)))
				exceptions.Add(new ArgumentException($"Collection contains item(s) with blank {nameof(Item.ProductId)}", nameof(items)));

			// this can happen with podcast episodes
			foreach (var i in items.Where(i => string.IsNullOrWhiteSpace(i.Title)))
				i.Title = "[blank title]";

			return exceptions;
		}
	}
	public class CategoryValidator : IValidator
	{
		public IEnumerable<Exception> Validate(IEnumerable<Item> items)
		{
			var exceptions = new List<Exception>();

			var distinct = items.GetCategoriesDistinct();
			if (distinct.Any(s => s.CategoryId is null))
				exceptions.Add(new ArgumentException($"Collection contains {nameof(Item.Categories)} with null {nameof(Ladder.CategoryId)}", nameof(items)));
			if (distinct.Any(s => s.CategoryName is null))
				exceptions.Add(new ArgumentException($"Collection contains {nameof(Item.Categories)} with null {nameof(Ladder.CategoryName)}", nameof(items)));

			return exceptions;
		}
	}
	public class ContributorValidator : IValidator
	{
		public IEnumerable<Exception> Validate(IEnumerable<Item> items)
		{
			var exceptions = new List<Exception>();

			if (items.GetAuthorsDistinct().Any(a => string.IsNullOrWhiteSpace(a.Name)))
				exceptions.Add(new ArgumentException($"Collection contains {nameof(Item.Authors)} with null {nameof(Person.Name)}", nameof(items)));
			if (items.GetNarratorsDistinct().Any(a => string.IsNullOrWhiteSpace(a.Name)))
				exceptions.Add(new ArgumentException($"Collection contains {nameof(Item.Narrators)} with null {nameof(Person.Name)}", nameof(items)));

			return exceptions;
		}
	}
	public class SeriesValidator : IValidator
	{
		public IEnumerable<Exception> Validate(IEnumerable<Item> items)
		{
			var exceptions = new List<Exception>();

			var distinct = items.GetSeriesDistinct();
			if (distinct.Any(s => s.SeriesId is null))
				exceptions.Add(new ArgumentException($"Collection contains {nameof(Item.Series)} with null {nameof(Series.SeriesId)}", nameof(items)));

			//// unfortunately, an actual user has a series with no name
			//if (distinct.Any(s => s.SeriesName is null))
			//    exceptions.Add(new ArgumentException($"Collection contains {nameof(Item.Series)} with null {nameof(Series.SeriesName)}", nameof(items)));

			return exceptions;
		}
	}
}
