﻿using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NuGet.Versioning;
using Squirrel.NuGet;

namespace Squirrel.CommandLine
{
    internal static class SystemCommandLineExtensions
    {
        public static string ToFullNameOrNull(this FileSystemInfo fsi)
        {
            return fsi?.FullName;
        }

        public static string ToAbsoluteOrNull(this Uri uri)
        {
            if (uri?.IsAbsoluteUri == true) return uri.AbsoluteUri;
            return null;
        }

        public static Option<T> SetDescription<T>(this Option<T> option, string description)
        {
            option.Description = description;
            return option;
        }

        public static Option<T> SetHidden<T>(this Option<T> option, bool isHidden = true)
        {
            option.IsHidden = isHidden;
            return option;
        }

        public static Option<T> SetRequired<T>(this Option<T> option, bool isRequired = true)
        {
            option.IsRequired = isRequired;
            return option;
        }

        public static Option<T> SetDefault<T>(this Option<T> option, T defaultValue)
        {
            option.SetDefaultValue(defaultValue);
            return option;
        }

        public static Option<T> SetArgumentHelpName<T>(this Option<T> option, string argumentHelpName)
        {
            option.ArgumentHelpName = argumentHelpName;
            return option;
        }

        public static Option<int> MustBeBetween(this Option<int> option, int minimum, int maximum)
        {
            option.AddValidator(x => Validate.MustBeBetween(x, minimum, maximum));
            return option;
        }

        public static Option<Uri> MustBeValidHttpUri(this Option<Uri> option)
        {
            option.RequiresScheme(Uri.UriSchemeHttp, Uri.UriSchemeHttps).RequiresAbsolute();
            return option;
        }

        public static Option<FileInfo> RequiresExtension(this Option<FileInfo> option, string extension)
        {
            option.AddValidator(x => Validate.RequiresExtension(x, extension));
            return option;
        }

        public static Option<DirectoryInfo> RequiresExtension(this Option<DirectoryInfo> option, string extension)
        {
            option.AddValidator(x => Validate.RequiresExtension(x, extension));
            return option;
        }

        public static Command AreMutuallyExclusive(this Command command, params Option[] options)
        {
            command.AddValidator(x => Validate.AreMutuallyExclusive(x, options));
            return command;
        }

        //public static Command RequiredAllowObsoleteFallback(this Command command, Option option, Option obsoleteOption)
        //{
        //    command.AddValidator(x => Validate.AtLeastOneRequired(x, new[] { option, obsoleteOption }, true));
        //    return command;
        //}

        public static Command AtLeastOneRequired(this Command command, params Option[] options)
        {
            command.AddValidator(x => Validate.AtLeastOneRequired(x, options, false));
            return command;
        }

        public static Option<string> MustContain(this Option<string> option, string value)
        {
            option.AddValidator(x => Validate.MustContain(x, value));
            return option;
        }

        public static Option<Uri> RequiresScheme(this Option<Uri> option, params string[] validSchemes)
        {
            option.AddValidator(x => Validate.RequiresScheme(x, validSchemes));
            return option;
        }

        public static Option<Uri> RequiresAbsolute(this Option<Uri> option, params string[] validSchemes)
        {
            option.AddValidator(Validate.RequiresAbsolute);
            return option;
        }

        public static Option<string> RequiresValidNuGetId(this Option<string> option)
        {
            option.AddValidator(Validate.RequiresValidNuGetId);
            return option;
        }

        //TODO: Could setup the options to accept type SemanticVersion and apply an appropriate parser for it
        public static Option<string> RequiresSemverCompliant(this Option<string> option)
        {
            option.AddValidator(Validate.RequiresSemverCompliant);
            return option;
        }

        public static Option<DirectoryInfo> MustNotBeEmpty(this Option<DirectoryInfo> option)
        {
            option.AddValidator(Validate.MustNotBeEmpty);
            return option;
        }

        public static Option<string> MustBeValidFrameworkString(this Option<string> option)
        {
            option.AddValidator(Validate.MustBeValidFrameworkString);
            return option;
        }

        public static Option<string> MustBeValidMsiVersion(this Option<string> option)
        {
            option.AddValidator(Validate.MustBeValidMsiVersion);
            return option;
        }

        public static Option<string> MustBeSupportedRid(this Option<string> option)
        {
            option.AddValidator(Validate.MustBeSupportedRid);
            return option;
        }

        private static class Validate
        {
            public static void MustBeBetween(OptionResult result, int minimum, int maximum)
            {
                for (int i = 0; i < result.Tokens.Count; i++) {
                    if (int.TryParse(result.Tokens[i].Value, out int value)) {
                        if (value < minimum || value > maximum) {
                            result.ErrorMessage = $"The value for {result.Token.Value} must be greater than {minimum} and less than {maximum}";
                            break;
                        }
                    } else {
                        result.ErrorMessage = $"{result.Tokens[i].Value} is not a valid integer for {result.Token.Value}";
                        break;
                    }
                }
            }

            public static void RequiresExtension(OptionResult result, string extension)
            {
                for (int i = 0; i < result.Tokens.Count; i++) {
                    if (!string.Equals(Path.GetExtension(result.Tokens[i].Value), extension, StringComparison.InvariantCultureIgnoreCase)) {
                        result.ErrorMessage = $"{result.Token.Value} does not have an {extension} extension";
                        break;
                    }
                }
            }

            public static void AreMutuallyExclusive(CommandResult result, Option[] options)
            {
                var specifiedOptions = options
                    .Where(x => result.FindResultFor(x) is not null)
                    .ToList();
                if (specifiedOptions.Count > 1) {
                    string optionsString = string.Join(" and ", specifiedOptions.Select(x => $"'{x.Aliases.First()}'"));
                    result.ErrorMessage = $"Cannot use {optionsString} options together, please choose one.";
                }
            }

            public static void AtLeastOneRequired(CommandResult result, Option[] options, bool onlyShowFirst = false)
            {
                var anySpecifiedOptions = options
                    .Any(x => result.FindResultFor(x) is not null);
                if (!anySpecifiedOptions) {
                    if (onlyShowFirst) {
                        result.ErrorMessage = $"Required argument missing for option: {options.First().Aliases.First()}";
                    } else {
                        string optionsString = string.Join(" and ", options.Select(x => $"'{x.Aliases.First()}'"));
                        result.ErrorMessage = $"At least one of the following options are required {optionsString}.";
                    }
                }
            }

            public static void MustContain(OptionResult result, string value)
            {
                for (int i = 0; i < result.Tokens.Count; i++) {
                    if (result.Tokens[i].Value?.Contains(value) == false) {
                        result.ErrorMessage = $"{result.Token.Value} must contain '{value}'. Current value is '{result.Tokens[i].Value}'";
                        break;
                    }
                }
            }

            public static void RequiresScheme(OptionResult result, string[] validSchemes)
            {
                for (int i = 0; i < result.Tokens.Count; i++) {
                    if (Uri.TryCreate(result.Tokens[i].Value, UriKind.RelativeOrAbsolute, out Uri uri) &&
                        uri.IsAbsoluteUri &&
                        !validSchemes.Contains(uri.Scheme)) {
                        result.ErrorMessage = $"{result.Token.Value} must contain a Uri with one of the following schems: {string.Join(", ", validSchemes)}. Current value is '{result.Tokens[i].Value}'";
                        break;
                    }
                }
            }

            public static void RequiresAbsolute(OptionResult result)
            {
                for (int i = 0; i < result.Tokens.Count; i++) {
                    if (!Uri.TryCreate(result.Tokens[i].Value, UriKind.Absolute, out Uri _)) {
                        result.ErrorMessage = $"{result.Token.Value} must contain an absolute Uri. Current value is '{result.Tokens[i].Value}'";
                        break;
                    }
                }
            }

            public static void RequiresValidNuGetId(OptionResult result)
            {
                for (int i = 0; i < result.Tokens.Count; i++) {
                    if (!NugetUtil.IsValidNuGetId(result.Tokens[i].Value)) {
                        result.ErrorMessage = $"{result.Token.Value} is an invalid NuGet package id. It must contain only alphanumeric characters, underscores, dashes, and dots.. Current value is '{result.Tokens[i].Value}'";
                        break;
                    }
                }
            }

            public static void RequiresSemverCompliant(OptionResult result)
            {
                string specifiedAlias = result.Token.Value;

                for (int i = 0; i < result.Tokens.Count; i++) {
                    string version = result.Tokens[i].Value;
                    //TODO: This is duplicating NugetUtil.ThrowIfVersionNotSemverCompliant
                    if (SemanticVersion.TryParse(version, out var parsed)) {
                        if (parsed < new SemanticVersion(0, 0, 1)) {
                            result.ErrorMessage = $"{result.Token.Value} contains an invalid package version '{version}', it must be >= 0.0.1.";
                            break;
                        }
                    } else {
                        result.ErrorMessage = $"{result.Token.Value} contains an invalid package version '{version}', it must be a 3-part SemVer2 compliant version string.";
                        break;
                    }
                }
            }

            public static void MustNotBeEmpty(OptionResult result)
            {
                for (int i = 0; i < result.Tokens.Count; i++) {
                    var token = result.Tokens[i];

                    if (!Directory.Exists(token.Value) ||
                        !Directory.EnumerateFileSystemEntries(token.Value).Any()) {
                        result.ErrorMessage = $"{result.Token.Value} must be a non-empty directory, but the specified directory '{token.Value}' was empty.";
                        return;
                    }
                }
            }

            public static void MustBeValidFrameworkString(OptionResult result)
            {
                for (var i = 0; i < result.Tokens.Count; i++) {
                    var framework = result.Tokens[i].Value;
                    try {
                        Runtimes.ParseDependencyString(framework);
                    } catch (Exception e) {
                        result.ErrorMessage = e.Message;
                    }
                }
            }

            public static void MustBeValidMsiVersion(OptionResult result)
            {
                for (var i = 0; i < result.Tokens.Count; i++) {
                    var version = result.Tokens[i].Value;
                    if (Version.TryParse(version, out var parsed)) {
                        if (parsed.Major > 255 || parsed.Minor > 255 || parsed.Build > 65535 || parsed.Revision > 0) {
                            result.ErrorMessage = $"MSI ProductVersion out of bounds '{version}'. Valid range is [0-255].[0-255].[0-65535].[0]";
                        }
                    } else {
                        result.ErrorMessage = "Version string is invalid / could not be parsed.";
                        break;
                    }
                }
            }

            public static void MustBeSupportedRid(OptionResult result)
            {
                for (int i = 0; i < result.Tokens.Count; i++) {
                    if (!Regex.IsMatch(result.Tokens[i].Value, @"^(?<os>osx|win)\.?(?<ver>[\d\.]+)?(?:-(?<arch>(?:x|arm)\d{2}))$"))
                        result.ErrorMessage = $"Invalid or unsupported runtime '{result.Token.Value}'. Valid example: win-x64, osx-arm64.";
                    break;
                }
            }
        }
    }
}
