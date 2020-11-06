// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Lists;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Framework.Utils;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Beatmaps
{
    /// <summary>
    /// A component which performs and acts as a central cache for difficulty calculations of beatmap/ruleset/mod combinations.
    /// Currently not persisted between game sessions.
    /// </summary>
    public class BeatmapDifficultyCache : MemoryCachingComponent<BeatmapDifficultyCache.DifficultyCacheLookup, StarDifficulty>
    {
        // Too many simultaneous updates can lead to stutters. One thread seems to work fine for song select display purposes.
        private readonly ThreadedTaskScheduler updateScheduler = new ThreadedTaskScheduler(1, nameof(BeatmapDifficultyCache));

        // All bindables that should be updated along with the current ruleset + mods.
        private readonly LockedWeakList<BindableStarDifficulty> trackedBindables = new LockedWeakList<BindableStarDifficulty>();

        [Resolved]
        private BeatmapManager beatmapManager { get; set; }

        [Resolved]
        private Bindable<RulesetInfo> currentRuleset { get; set; }

        [Resolved]
        private Bindable<IReadOnlyList<Mod>> currentMods { get; set; }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            currentRuleset.BindValueChanged(_ => updateTrackedBindables());
            currentMods.BindValueChanged(_ => updateTrackedBindables(), true);
        }

        /// <summary>
        /// Retrieves a bindable containing the star difficulty of a <see cref="BeatmapInfo"/> that follows the currently-selected ruleset and mods.
        /// </summary>
        /// <param name="beatmapInfo">The <see cref="BeatmapInfo"/> to get the difficulty of.</param>
        /// <param name="cancellationToken">An optional <see cref="CancellationToken"/> which stops updating the star difficulty for the given <see cref="BeatmapInfo"/>.</param>
        /// <returns>A bindable that is updated to contain the star difficulty when it becomes available.</returns>
        public IBindable<StarDifficulty> GetBindableDifficulty([NotNull] BeatmapInfo beatmapInfo, CancellationToken cancellationToken = default)
        {
            var bindable = createBindable(beatmapInfo, currentRuleset.Value, currentMods.Value, cancellationToken);
            trackedBindables.Add(bindable);
            return bindable;
        }

        /// <summary>
        /// Retrieves a bindable containing the star difficulty of a <see cref="BeatmapInfo"/> with a given <see cref="RulesetInfo"/> and <see cref="Mod"/> combination.
        /// </summary>
        /// <remarks>
        /// The bindable will not update to follow the currently-selected ruleset and mods.
        /// </remarks>
        /// <param name="beatmapInfo">The <see cref="BeatmapInfo"/> to get the difficulty of.</param>
        /// <param name="rulesetInfo">The <see cref="RulesetInfo"/> to get the difficulty with. If <c>null</c>, the <paramref name="beatmapInfo"/>'s ruleset is used.</param>
        /// <param name="mods">The <see cref="Mod"/>s to get the difficulty with. If <c>null</c>, no mods will be assumed.</param>
        /// <param name="cancellationToken">An optional <see cref="CancellationToken"/> which stops updating the star difficulty for the given <see cref="BeatmapInfo"/>.</param>
        /// <returns>A bindable that is updated to contain the star difficulty when it becomes available.</returns>
        public IBindable<StarDifficulty> GetBindableDifficulty([NotNull] BeatmapInfo beatmapInfo, [CanBeNull] RulesetInfo rulesetInfo, [CanBeNull] IEnumerable<Mod> mods,
                                                               CancellationToken cancellationToken = default)
            => createBindable(beatmapInfo, rulesetInfo, mods, cancellationToken);

        /// <summary>
        /// Retrieves the difficulty of a <see cref="BeatmapInfo"/>.
        /// </summary>
        /// <param name="beatmapInfo">The <see cref="BeatmapInfo"/> to get the difficulty of.</param>
        /// <param name="rulesetInfo">The <see cref="RulesetInfo"/> to get the difficulty with.</param>
        /// <param name="mods">The <see cref="Mod"/>s to get the difficulty with.</param>
        /// <param name="cancellationToken">An optional <see cref="CancellationToken"/> which stops computing the star difficulty.</param>
        /// <returns>The <see cref="StarDifficulty"/>.</returns>
        public async Task<StarDifficulty> GetDifficultyAsync([NotNull] BeatmapInfo beatmapInfo, [CanBeNull] RulesetInfo rulesetInfo = null, [CanBeNull] IEnumerable<Mod> mods = null,
                                                             CancellationToken cancellationToken = default)
        {
            if (tryGetExisting(beatmapInfo, rulesetInfo, mods, out var existing, out var key))
                return existing;

            return await Task.Factory.StartNew(() =>
            {
                // Computation may have finished in a previous task.
                if (tryGetExisting(beatmapInfo, rulesetInfo, mods, out existing, out _))
                    return existing;

                return computeDifficulty(key, beatmapInfo, rulesetInfo);
            }, cancellationToken, TaskCreationOptions.HideScheduler | TaskCreationOptions.RunContinuationsAsynchronously, updateScheduler);
        }

        /// <summary>
        /// Retrieves the difficulty of a <see cref="BeatmapInfo"/>.
        /// </summary>
        /// <param name="beatmapInfo">The <see cref="BeatmapInfo"/> to get the difficulty of.</param>
        /// <param name="rulesetInfo">The <see cref="RulesetInfo"/> to get the difficulty with.</param>
        /// <param name="mods">The <see cref="Mod"/>s to get the difficulty with.</param>
        /// <returns>The <see cref="StarDifficulty"/>.</returns>
        public StarDifficulty GetDifficulty([NotNull] BeatmapInfo beatmapInfo, [CanBeNull] RulesetInfo rulesetInfo = null, [CanBeNull] IEnumerable<Mod> mods = null)
        {
            if (tryGetExisting(beatmapInfo, rulesetInfo, mods, out var existing, out var key))
                return existing;

            return computeDifficulty(key, beatmapInfo, rulesetInfo);
        }

        /// <summary>
        /// Retrieves the <see cref="DifficultyRating"/> that describes a star rating.
        /// </summary>
        /// <remarks>
        /// For more information, see: https://osu.ppy.sh/help/wiki/Difficulties
        /// </remarks>
        /// <param name="starRating">The star rating.</param>
        /// <returns>The <see cref="DifficultyRating"/> that best describes <paramref name="starRating"/>.</returns>
        public static DifficultyRating GetDifficultyRating(double starRating)
        {
            if (Precision.AlmostBigger(starRating, 6.5, 0.005))
                return DifficultyRating.ExpertPlus;

            if (Precision.AlmostBigger(starRating, 5.3, 0.005))
                return DifficultyRating.Expert;

            if (Precision.AlmostBigger(starRating, 4.0, 0.005))
                return DifficultyRating.Insane;

            if (Precision.AlmostBigger(starRating, 2.7, 0.005))
                return DifficultyRating.Hard;

            if (Precision.AlmostBigger(starRating, 2.0, 0.005))
                return DifficultyRating.Normal;

            return DifficultyRating.Easy;
        }

        private CancellationTokenSource trackedUpdateCancellationSource;
        private readonly List<CancellationTokenSource> linkedCancellationSources = new List<CancellationTokenSource>();

        /// <summary>
        /// Updates all tracked <see cref="BindableStarDifficulty"/> using the current ruleset and mods.
        /// </summary>
        private void updateTrackedBindables()
        {
            cancelTrackedBindableUpdate();
            trackedUpdateCancellationSource = new CancellationTokenSource();

            foreach (var b in trackedBindables)
            {
                var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(trackedUpdateCancellationSource.Token, b.CancellationToken);
                linkedCancellationSources.Add(linkedSource);

                updateBindable(b, currentRuleset.Value, currentMods.Value, linkedSource.Token);
            }
        }

        /// <summary>
        /// Cancels the existing update of all tracked <see cref="BindableStarDifficulty"/> via <see cref="updateTrackedBindables"/>.
        /// </summary>
        private void cancelTrackedBindableUpdate()
        {
            trackedUpdateCancellationSource?.Cancel();
            trackedUpdateCancellationSource = null;

            if (linkedCancellationSources != null)
            {
                foreach (var c in linkedCancellationSources)
                    c.Dispose();

                linkedCancellationSources.Clear();
            }
        }

        /// <summary>
        /// Creates a new <see cref="BindableStarDifficulty"/> and triggers an initial value update.
        /// </summary>
        /// <param name="beatmapInfo">The <see cref="BeatmapInfo"/> that star difficulty should correspond to.</param>
        /// <param name="initialRulesetInfo">The initial <see cref="RulesetInfo"/> to get the difficulty with.</param>
        /// <param name="initialMods">The initial <see cref="Mod"/>s to get the difficulty with.</param>
        /// <param name="cancellationToken">An optional <see cref="CancellationToken"/> which stops updating the star difficulty for the given <see cref="BeatmapInfo"/>.</param>
        /// <returns>The <see cref="BindableStarDifficulty"/>.</returns>
        private BindableStarDifficulty createBindable([NotNull] BeatmapInfo beatmapInfo, [CanBeNull] RulesetInfo initialRulesetInfo, [CanBeNull] IEnumerable<Mod> initialMods,
                                                      CancellationToken cancellationToken)
        {
            var bindable = new BindableStarDifficulty(beatmapInfo, cancellationToken);
            updateBindable(bindable, initialRulesetInfo, initialMods, cancellationToken);
            return bindable;
        }

        /// <summary>
        /// Updates the value of a <see cref="BindableStarDifficulty"/> with a given ruleset + mods.
        /// </summary>
        /// <param name="bindable">The <see cref="BindableStarDifficulty"/> to update.</param>
        /// <param name="rulesetInfo">The <see cref="RulesetInfo"/> to update with.</param>
        /// <param name="mods">The <see cref="Mod"/>s to update with.</param>
        /// <param name="cancellationToken">A token that may be used to cancel this update.</param>
        private void updateBindable([NotNull] BindableStarDifficulty bindable, [CanBeNull] RulesetInfo rulesetInfo, [CanBeNull] IEnumerable<Mod> mods, CancellationToken cancellationToken = default)
        {
            GetDifficultyAsync(bindable.Beatmap, rulesetInfo, mods, cancellationToken).ContinueWith(t =>
            {
                // We're on a threadpool thread, but we should exit back to the update thread so consumers can safely handle value-changed events.
                Schedule(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                        bindable.Value = t.Result;
                });
            }, cancellationToken);
        }

        /// <summary>
        /// Computes the difficulty defined by a <see cref="DifficultyCacheLookup"/> key, and stores it to the timed cache.
        /// </summary>
        /// <param name="key">The <see cref="DifficultyCacheLookup"/> that defines the computation parameters.</param>
        /// <param name="beatmapInfo">The <see cref="BeatmapInfo"/> to compute the difficulty of.</param>
        /// <param name="rulesetInfo">The <see cref="RulesetInfo"/> to compute the difficulty with.</param>
        /// <returns>The <see cref="StarDifficulty"/>.</returns>
        private StarDifficulty computeDifficulty(in DifficultyCacheLookup key, BeatmapInfo beatmapInfo, RulesetInfo rulesetInfo)
        {
            // In the case that the user hasn't given us a ruleset, use the beatmap's default ruleset.
            rulesetInfo ??= beatmapInfo.Ruleset;

            try
            {
                var ruleset = rulesetInfo.CreateInstance();
                Debug.Assert(ruleset != null);

                var calculator = ruleset.CreateDifficultyCalculator(beatmapManager.GetWorkingBeatmap(beatmapInfo));
                var attributes = calculator.Calculate(key.Mods);

                return Cache[key] = new StarDifficulty(attributes);
            }
            catch (BeatmapInvalidForRulesetException e)
            {
                // Conversion has failed for the given ruleset, so return the difficulty in the beatmap's default ruleset.

                // Ensure the beatmap's default ruleset isn't the one already being converted to.
                // This shouldn't happen as it means something went seriously wrong, but if it does an endless loop should be avoided.
                if (rulesetInfo.Equals(beatmapInfo.Ruleset))
                {
                    Logger.Error(e, $"Failed to convert {beatmapInfo.OnlineBeatmapID} to the beatmap's default ruleset ({beatmapInfo.Ruleset}).");
                    return Cache[key] = new StarDifficulty();
                }

                // Check the cache first because this is now a different ruleset than the one previously guarded against.
                if (tryGetExisting(beatmapInfo, beatmapInfo.Ruleset, Array.Empty<Mod>(), out var existingDefault, out var existingDefaultKey))
                    return existingDefault;

                return computeDifficulty(existingDefaultKey, beatmapInfo, beatmapInfo.Ruleset);
            }
            catch
            {
                return Cache[key] = new StarDifficulty();
            }
        }

        /// <summary>
        /// Attempts to retrieve an existing difficulty for the combination.
        /// </summary>
        /// <param name="beatmapInfo">The <see cref="BeatmapInfo"/>.</param>
        /// <param name="rulesetInfo">The <see cref="RulesetInfo"/>.</param>
        /// <param name="mods">The <see cref="Mod"/>s.</param>
        /// <param name="existingDifficulty">The existing difficulty value, if present.</param>
        /// <param name="key">The <see cref="DifficultyCacheLookup"/> key that was used to perform this lookup. This can be further used to query <see cref="computeDifficulty"/>.</param>
        /// <returns>Whether an existing difficulty was found.</returns>
        private bool tryGetExisting(BeatmapInfo beatmapInfo, RulesetInfo rulesetInfo, IEnumerable<Mod> mods, out StarDifficulty existingDifficulty, out DifficultyCacheLookup key)
        {
            // In the case that the user hasn't given us a ruleset, use the beatmap's default ruleset.
            rulesetInfo ??= beatmapInfo.Ruleset;

            // Difficulty can only be computed if the beatmap and ruleset are locally available.
            if (beatmapInfo.ID == 0 || rulesetInfo.ID == null)
            {
                // If not, fall back to the existing star difficulty (e.g. from an online source).
                existingDifficulty = new StarDifficulty(beatmapInfo.StarDifficulty, beatmapInfo.MaxCombo ?? 0);
                key = default;

                return true;
            }

            key = new DifficultyCacheLookup(beatmapInfo.ID, rulesetInfo.ID.Value, mods);
            return Cache.TryGetValue(key, out existingDifficulty);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            cancelTrackedBindableUpdate();
            updateScheduler?.Dispose();
        }

        public readonly struct DifficultyCacheLookup : IEquatable<DifficultyCacheLookup>
        {
            public readonly int BeatmapId;
            public readonly int RulesetId;
            public readonly Mod[] Mods;

            public DifficultyCacheLookup(int beatmapId, int rulesetId, IEnumerable<Mod> mods)
            {
                BeatmapId = beatmapId;
                RulesetId = rulesetId;
                Mods = mods?.OrderBy(m => m.Acronym).ToArray() ?? Array.Empty<Mod>();
            }

            public bool Equals(DifficultyCacheLookup other)
                => BeatmapId == other.BeatmapId
                   && RulesetId == other.RulesetId
                   && Mods.Select(m => m.Acronym).SequenceEqual(other.Mods.Select(m => m.Acronym));

            public override int GetHashCode()
            {
                var hashCode = new HashCode();

                hashCode.Add(BeatmapId);
                hashCode.Add(RulesetId);
                foreach (var mod in Mods)
                    hashCode.Add(mod.Acronym);

                return hashCode.ToHashCode();
            }
        }

        private class BindableStarDifficulty : Bindable<StarDifficulty>
        {
            public readonly BeatmapInfo Beatmap;
            public readonly CancellationToken CancellationToken;

            public BindableStarDifficulty(BeatmapInfo beatmap, CancellationToken cancellationToken)
            {
                Beatmap = beatmap;
                CancellationToken = cancellationToken;
            }
        }
    }

    public readonly struct StarDifficulty
    {
        /// <summary>
        /// The star difficulty rating for the given beatmap.
        /// </summary>
        public readonly double Stars;

        /// <summary>
        /// The maximum combo achievable on the given beatmap.
        /// </summary>
        public readonly int MaxCombo;

        /// <summary>
        /// The difficulty attributes computed for the given beatmap.
        /// Might not be available if the star difficulty is associated with a beatmap that's not locally available.
        /// </summary>
        [CanBeNull]
        public readonly DifficultyAttributes Attributes;

        /// <summary>
        /// Creates a <see cref="StarDifficulty"/> structure based on <see cref="DifficultyAttributes"/> computed
        /// by a <see cref="DifficultyCalculator"/>.
        /// </summary>
        public StarDifficulty([NotNull] DifficultyAttributes attributes)
        {
            Stars = attributes.StarRating;
            MaxCombo = attributes.MaxCombo;
            Attributes = attributes;
            // Todo: Add more members (BeatmapInfo.DifficultyRating? Attributes? Etc...)
        }

        /// <summary>
        /// Creates a <see cref="StarDifficulty"/> structure with a pre-populated star difficulty and max combo
        /// in scenarios where computing <see cref="DifficultyAttributes"/> is not feasible (i.e. when working with online sources).
        /// </summary>
        public StarDifficulty(double starDifficulty, int maxCombo)
        {
            Stars = starDifficulty;
            MaxCombo = maxCombo;
            Attributes = null;
        }

        public DifficultyRating DifficultyRating => BeatmapDifficultyCache.GetDifficultyRating(Stars);
    }
}
