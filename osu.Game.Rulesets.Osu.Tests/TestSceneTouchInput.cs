﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Input.States;
using osu.Framework.Testing;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public partial class TestSceneTouchInput : OsuManualInputManagerTestScene
    {
        private TestActionKeyCounter leftKeyCounter = null!;

        private TestActionKeyCounter rightKeyCounter = null!;

        private OsuInputManager osuInputManager = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            releaseAllTouches();

            AddStep("Create tests", () =>
            {
                Children = new Drawable[]
                {
                    osuInputManager = new OsuInputManager(new OsuRuleset().RulesetInfo)
                    {
                        Child = new Container
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Children = new Drawable[]
                            {
                                leftKeyCounter = new TestActionKeyCounter(OsuAction.LeftButton)
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.CentreRight,
                                    X = -100,
                                },
                                rightKeyCounter = new TestActionKeyCounter(OsuAction.RightButton)
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.CentreLeft,
                                    X = 100,
                                }
                            },
                        }
                    },
                    new TouchVisualiser(),
                };
            });
        }

        [Test]
        public void TestSimpleInput()
        {
            beginTouch(TouchSource.Touch1);

            assertKeyCounter(1, 0);
            expectPressedCurrently(OsuAction.LeftButton);

            beginTouch(TouchSource.Touch2);

            assertKeyCounter(1, 1);
            expectPressedCurrently(OsuAction.LeftButton);
            expectPressedCurrently(OsuAction.RightButton);

            // Subsequent touches should be ignored.
            beginTouch(TouchSource.Touch3);
            beginTouch(TouchSource.Touch4);

            assertKeyCounter(1, 1);

            expectPressedCurrently(OsuAction.LeftButton);
            expectPressedCurrently(OsuAction.RightButton);

            assertKeyCounter(1, 1);
        }

        [Test]
        public void TestAlternatingInput()
        {
            beginTouch(TouchSource.Touch1);

            assertKeyCounter(1, 0);
            expectPressedCurrently(OsuAction.LeftButton);

            beginTouch(TouchSource.Touch2);

            assertKeyCounter(1, 1);
            expectPressedCurrently(OsuAction.LeftButton);
            expectPressedCurrently(OsuAction.RightButton);

            endTouch(TouchSource.Touch1);

            assertKeyCounter(1, 1);
            expectPressedCurrently(OsuAction.RightButton);

            beginTouch(TouchSource.Touch1);

            assertKeyCounter(2, 1);
            expectPressedCurrently(OsuAction.LeftButton);
            expectPressedCurrently(OsuAction.RightButton);

            endTouch(TouchSource.Touch2);

            assertKeyCounter(2, 1);
            expectPressedCurrently(OsuAction.LeftButton);

            beginTouch(TouchSource.Touch2);

            assertKeyCounter(2, 2);
            expectPressedCurrently(OsuAction.LeftButton);
            expectPressedCurrently(OsuAction.RightButton);
        }

        [Test]
        public void TestPressReleaseOrder()
        {
            beginTouch(TouchSource.Touch1);
            beginTouch(TouchSource.Touch2);
            beginTouch(TouchSource.Touch3);

            assertKeyCounter(1, 1);
            expectPressedCurrently(OsuAction.LeftButton);
            expectPressedCurrently(OsuAction.RightButton);

            // Touch 3 was ignored, but let's ensure that if 1 or 2 are released, 3 will be handled a second attempt.
            endTouch(TouchSource.Touch1);

            assertKeyCounter(1, 1);
            expectPressedCurrently(OsuAction.RightButton);

            endTouch(TouchSource.Touch3);

            assertKeyCounter(1, 1);
            expectPressedCurrently(OsuAction.RightButton);

            beginTouch(TouchSource.Touch3);

            assertKeyCounter(2, 1);
            expectPressedCurrently(OsuAction.LeftButton);
            expectPressedCurrently(OsuAction.RightButton);
        }

        [Test]
        public void TestWithDisallowedUserCursor()
        {
            beginTouch(TouchSource.Touch1);

            assertKeyCounter(1, 0);
            expectPressedCurrently(OsuAction.LeftButton);

            beginTouch(TouchSource.Touch2);

            assertKeyCounter(1, 1);
            expectPressedCurrently(OsuAction.RightButton);

            // Subsequent touches should be ignored.
            beginTouch(TouchSource.Touch3);
            beginTouch(TouchSource.Touch4);

            assertKeyCounter(1, 1);

            expectPressedCurrently(OsuAction.LeftButton);
            expectPressedCurrently(OsuAction.RightButton);

            assertKeyCounter(1, 1);
        }

        private void beginTouch(TouchSource source, Vector2? screenSpacePosition = null) =>
            AddStep($"Begin touch for {source}", () => InputManager.BeginTouch(new Touch(source, screenSpacePosition ??= getSanePositionForSource(source))));

        private void endTouch(TouchSource source, Vector2? screenSpacePosition = null) =>
            AddStep($"Release touch for {source}", () => InputManager.EndTouch(new Touch(source, screenSpacePosition ??= getSanePositionForSource(source))));

        private Vector2 getSanePositionForSource(TouchSource source)
        {
            return new Vector2(
                osuInputManager.ScreenSpaceDrawQuad.Centre.X + osuInputManager.ScreenSpaceDrawQuad.Width * (-1 + (int)source) / 8,
                osuInputManager.ScreenSpaceDrawQuad.Centre.Y - 100
            );
        }

        private void assertKeyCounter(int left, int right)
        {
            AddAssert($"The left key was pressed {left} times", () => leftKeyCounter.CountPresses, () => Is.EqualTo(left));
            AddAssert($"The right key was pressed {right} times", () => rightKeyCounter.CountPresses, () => Is.EqualTo(right));
        }

        private void releaseAllTouches()
        {
            AddStep("Release all touches", () =>
            {
                foreach (TouchSource source in InputManager.CurrentState.Touch.ActiveSources)
                    InputManager.EndTouch(new Touch(source, osuInputManager.ScreenSpaceDrawQuad.Centre));
            });
        }

        private void expectPressedCurrently(OsuAction action) => AddAssert($"Is pressing {action}", () => osuInputManager.PressedActions.Contains(action));

        public partial class TestActionKeyCounter : KeyCounter, IKeyBindingHandler<OsuAction>
        {
            public OsuAction Action { get; }

            public TestActionKeyCounter(OsuAction action)
                : base(action.ToString())
            {
                Action = action;
            }

            public bool OnPressed(KeyBindingPressEvent<OsuAction> e)
            {
                if (e.Action == Action)
                {
                    IsLit = true;
                    Increment();
                }

                return false;
            }

            public void OnReleased(KeyBindingReleaseEvent<OsuAction> e)
            {
                if (e.Action == Action) IsLit = false;
            }
        }

        public partial class TouchVisualiser : CompositeDrawable
        {
            private readonly Drawable?[] drawableTouches = new Drawable?[10];

            public TouchVisualiser()
            {
                RelativeSizeAxes = Axes.Both;
            }

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

            protected override bool OnTouchDown(TouchDownEvent e)
            {
                if (IsDisposed)
                    return false;

                var circle = new Circle
                {
                    Alpha = 0.5f,
                    Origin = Anchor.Centre,
                    Size = new Vector2(20),
                    Position = e.Touch.Position,
                    Colour = colourFor(e.Touch.Source),
                };

                AddInternal(circle);
                drawableTouches[(int)e.Touch.Source] = circle;
                return false;
            }

            protected override void OnTouchMove(TouchMoveEvent e)
            {
                if (IsDisposed)
                    return;

                var circle = drawableTouches[(int)e.Touch.Source];

                Debug.Assert(circle != null);

                AddInternal(new FadingCircle(circle));
                circle.Position = e.Touch.Position;
            }

            protected override void OnTouchUp(TouchUpEvent e)
            {
                var circle = drawableTouches[(int)e.Touch.Source];
                circle.FadeOut(200, Easing.OutQuint).Expire();
                drawableTouches[(int)e.Touch.Source] = null;
            }

            private Color4 colourFor(TouchSource source)
            {
                return Color4.FromHsv(new Vector4((float)source / TouchState.MAX_TOUCH_COUNT, 1f, 1f, 1f));
            }

            private partial class FadingCircle : Circle
            {
                public FadingCircle(Drawable source)
                {
                    Origin = Anchor.Centre;
                    Size = source.Size;
                    Position = source.Position;
                    Colour = source.Colour;
                }

                protected override void LoadComplete()
                {
                    base.LoadComplete();
                    this.FadeOut(200).Expire();
                }
            }
        }
    }
}
