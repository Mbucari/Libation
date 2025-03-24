﻿using NameParser;
using System;

#nullable enable
namespace LibationFileManager.Templates;

public class ContributorDto : IFormattable
{
	public HumanName HumanName { get; }
	public string? AudibleContributorId { get; }
	public ContributorDto(string name, string? audibleContributorId)
	{
		HumanName = new HumanName(RemoveSuffix(name), Prefer.FirstOverPrefix);
		AudibleContributorId = audibleContributorId;
	}

	public override string ToString()
		=> ToString("{T} {F} {M} {L} {S}", null);

	public string ToString(string? format, IFormatProvider? _)
	{
		if (string.IsNullOrWhiteSpace(format))
			return ToString();

		//Single-word names parse as first names. Use it as last name.
		var lastName = string.IsNullOrWhiteSpace(HumanName.Last) ? HumanName.First : HumanName.Last;

		return format
			.Replace("{T}", HumanName.Title)
			.Replace("{F}", HumanName.First)
			.Replace("{M}", HumanName.Middle)
			.Replace("{L}", lastName)
			.Replace("{S}", HumanName.Suffix)
			.Replace("{ID}", AudibleContributorId)
			.Trim();
	}

	private static string RemoveSuffix(string namesString)
	{
		namesString = namesString.Replace('’', '\'').Replace(" - Ret.", ", Ret.");
		int dashIndex = namesString.IndexOf(" - ");
		return (dashIndex > 0 ? namesString[..dashIndex] : namesString).Trim();
	}
}
