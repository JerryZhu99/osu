﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using osu.Framework.Testing;
using osu.Game.Models;
using osu.Game.Users;
using Realms;

#nullable enable

namespace osu.Game.Beatmaps
{
    [ExcludeFromDynamicCompile]
    [Serializable]
    [MapTo("BeatmapMetadata")]
    public class BeatmapMetadata : RealmObject, IBeatmapMetadataInfo
    {
        public string Title { get; set; } = string.Empty;

        [JsonProperty("title_unicode")]
        public string TitleUnicode { get; set; } = string.Empty;

        public string Artist { get; set; } = string.Empty;

        [JsonProperty("artist_unicode")]
        public string ArtistUnicode { get; set; } = string.Empty;

        public RealmUser Author { get; set; } = new RealmUser(); // TODO: not sure we want to initialise this only to have it overwritten by retrieval.

        public string Source { get; set; } = string.Empty;

        [JsonProperty(@"tags")]
        public string Tags { get; set; } = string.Empty;

        /// <summary>
        /// The time in milliseconds to begin playing the track for preview purposes.
        /// If -1, the track should begin playing at 40% of its length.
        /// </summary>
        public int PreviewTime { get; set; }

        public string AudioFile { get; set; } = string.Empty;
        public string BackgroundFile { get; set; } = string.Empty;

        IUser IBeatmapMetadataInfo.Author => Author;

        #region Compatibility properties

        public string AuthorString
        {
            get => Author.Username;
            set => Author.Username = value;
        }

        #endregion

        public override string ToString() => this.GetDisplayTitle();
    }
}
