// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Localisation;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects;


namespace osu.Game.Rulesets.Osu.Mods
{
    public class OsuModJuggle : Mod, IApplicableToDrawableHitObject, IApplicableToBeatmap
    {
        public override string Name => "Juggle";

        public override string Acronym => "JG";

        public override double ScoreMultiplier => 1;

        public override LocalisableString Description => "Reuse the notes!";

        //Alters the transforms of the approach circles, breaking the effects of these mods.
        public override Type[] IncompatibleMods => new[] { typeof(OsuModApproachDifferent) };

        public override ModType Type => ModType.Fun;

        [SettingSource("Objects Count", "How many objects are on screen", 0)]
        public Bindable<int> ObjectCount { get; } = new BindableInt(4)
        {
            MinValue = 1,
            MaxValue = 10
        };

        //mod breaks normal approach circle preempt
        private double originalPreempt;

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            var firstHitObject = beatmap.HitObjects.OfType<OsuHitObject>().FirstOrDefault();
            if (firstHitObject == null)
                return;
            originalPreempt = firstHitObject.TimePreempt;

            var objects = beatmap.HitObjects.OfType<OsuHitObject>().ToArray();

            OsuHitObject lastObject;
            for (int index = 0; index < objects.Count(); index++)
            {
                var obj = objects[index];
                if (index >= ObjectCount.Value)
                {
                    // startPositions.Add((int)obj.StartTime, objects[index - 3].Position);
                    lastObject = objects[index - ObjectCount.Value];
                    applyFadeInAdjustment(obj);
                }
                else
                {
                    obj.StartPosition = obj.Position;
                }

            }

            void applyFadeInAdjustment(OsuHitObject osuObject)
            {
                osuObject.TimePreempt = osuObject.StartTime - lastObject.GetEndTime();
                osuObject.TimeFadeIn = 0;
                osuObject.StartPosition = lastObject.EndPosition;
                foreach (var nested in osuObject.NestedHitObjects.OfType<OsuHitObject>())
                {
                    switch (nested)
                    {
                        //Freezing the SliderTicks doesnt play well with snaking sliders
                        case SliderTick:
                        //SliderRepeat wont layer correctly if preempt is changed.
                        case SliderRepeat:
                            break;

                        default:
                            applyFadeInAdjustment(nested);
                            break;
                    }
                }
            }
        }


        public void ApplyToDrawableHitObject(DrawableHitObject drawable)
        {
            drawable.OnUpdate += _ =>
            {
                switch (drawable)
                {
                    case DrawableSliderHead:
                    case DrawableSliderTail:
                    case DrawableSliderTick:
                    case DrawableSliderRepeat:
                        return;

                    default:
                        var hitObject = (OsuHitObject)drawable.HitObject;
                        double appearTime = hitObject.StartTime - hitObject.TimePreempt - 1;
                        double moveDuration = hitObject.TimePreempt / 2 + 1;

                        using (drawable.BeginAbsoluteSequence(appearTime))
                        {
                            drawable
                                .MoveTo(hitObject.StartPosition)
                                .MoveTo(hitObject.Position, moveDuration, Easing.OutSine);
                        }

                        break;
                }
            };
        }

    }
}
