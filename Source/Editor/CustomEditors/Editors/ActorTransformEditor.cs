// Copyright (c) 2012-2023 Wojciech Figat. All rights reserved.

using FlaxEngine;
using FlaxEngine.GUI;

namespace FlaxEditor.CustomEditors.Editors
{
    /// <summary>
    /// Actor transform editor.
    /// </summary>
    [HideInEditor]
    public static class ActorTransformEditor
    {
        /// <summary>
        /// The X axis color.
        /// </summary>
        public static Color AxisColorX = new Color(1.0f, 0.0f, 0.02745f, 1.0f);

        /// <summary>
        /// The Y axis color.
        /// </summary>
        public static Color AxisColorY = new Color(0.239215f, 1.0f, 0.047058f, 1.0f);

        /// <summary>
        /// The Z axis color.
        /// </summary>
        public static Color AxisColorZ = new Color(0.0f, 0.0235294f, 1.0f, 1.0f);

        /// <summary>
        /// Custom editor for actor position property.
        /// </summary>
        /// <seealso cref="FlaxEditor.CustomEditors.Editors.Vector3Editor" />
        public class PositionEditor : Vector3Editor
        {
            /// <inheritdoc />
            public override void Initialize(LayoutElementsContainer layout)
            {
                base.Initialize(layout);

                // Override colors
                var back = FlaxEngine.GUI.Style.Current.TextBoxBackground;
                var grayOutFactor = 0.6f;
                XElement.ValueBox.BorderColor = Color.Lerp(AxisColorX, back, grayOutFactor);
                XElement.ValueBox.BorderSelectedColor = AxisColorX;
                YElement.ValueBox.BorderColor = Color.Lerp(AxisColorY, back, grayOutFactor);
                YElement.ValueBox.BorderSelectedColor = AxisColorY;
                ZElement.ValueBox.BorderColor = Color.Lerp(AxisColorZ, back, grayOutFactor);
                ZElement.ValueBox.BorderSelectedColor = AxisColorZ;
            }
        }

        /// <summary>
        /// Custom editor for actor orientation property.
        /// </summary>
        /// <seealso cref="FlaxEditor.CustomEditors.Editors.QuaternionEditor" />
        public class OrientationEditor : QuaternionEditor
        {
            /// <inheritdoc />
            public override void Initialize(LayoutElementsContainer layout)
            {
                base.Initialize(layout);

                // Override colors
                var back = FlaxEngine.GUI.Style.Current.TextBoxBackground;
                var grayOutFactor = 0.6f;
                XElement.ValueBox.BorderColor = Color.Lerp(AxisColorX, back, grayOutFactor);
                XElement.ValueBox.BorderSelectedColor = AxisColorX;
                YElement.ValueBox.BorderColor = Color.Lerp(AxisColorY, back, grayOutFactor);
                YElement.ValueBox.BorderSelectedColor = AxisColorY;
                ZElement.ValueBox.BorderColor = Color.Lerp(AxisColorZ, back, grayOutFactor);
                ZElement.ValueBox.BorderSelectedColor = AxisColorZ;
            }
        }

        /// <summary>
        /// Custom editor for actor scale property.
        /// </summary>
        /// <seealso cref="FlaxEditor.CustomEditors.Editors.Float3Editor" />
        public class ScaleEditor : Float3Editor
        {
            private Image _linkImage;

            /// <inheritdoc />
            public override void Initialize(LayoutElementsContainer layout)
            {
                base.Initialize(layout);

                LinkValues = Editor.Instance.Windows.PropertiesWin.ScaleLinked;

                _linkImage = new Image
                {
                    Parent = LinkedLabel,
                    Width = 18,
                    Height = 18,
                    Brush = LinkValues ? new SpriteBrush(Editor.Instance.Icons.Link32) : new SpriteBrush(),
                    AnchorPreset = AnchorPresets.TopLeft,
                    TooltipText = "Scale values are linked together.",
                };
                var x = LinkedLabel.Text.Value.Length * 7 + 5;
                _linkImage.LocalX += x;
                _linkImage.LocalY += 1;

                LinkedLabel.SetupContextMenu += (label, menu, editor) =>
                {
                    menu.AddSeparator();
                    if (LinkValues)
                        menu.AddButton("Unlink", ToggleLink).LinkTooltip("Unlinks scale components from uniform scaling");
                    else
                        menu.AddButton("Link", ToggleLink).LinkTooltip("Links scale components for uniform scaling");
                };

                // Override colors
                var back = FlaxEngine.GUI.Style.Current.TextBoxBackground;
                var grayOutFactor = 0.6f;
                XElement.ValueBox.BorderColor = Color.Lerp(AxisColorX, back, grayOutFactor);
                XElement.ValueBox.BorderSelectedColor = AxisColorX;
                YElement.ValueBox.BorderColor = Color.Lerp(AxisColorY, back, grayOutFactor);
                YElement.ValueBox.BorderSelectedColor = AxisColorY;
                ZElement.ValueBox.BorderColor = Color.Lerp(AxisColorZ, back, grayOutFactor);
                ZElement.ValueBox.BorderSelectedColor = AxisColorZ;
            }

            /// <summary>
            /// Toggles the linking functionality.
            /// </summary>
            public void ToggleLink()
            {
                LinkValues = !LinkValues;
                Editor.Instance.Windows.PropertiesWin.ScaleLinked = LinkValues;
                _linkImage.Brush = LinkValues ? new SpriteBrush(Editor.Instance.Icons.Link32) : new SpriteBrush();
            }
        }
    }
}
