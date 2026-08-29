using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Actions;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Interactable;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.Core.UISystem.Controls.Text.Editing;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Vulkan;
using System.ComponentModel;
using System.Reflection;

namespace ArctisAurora.Core.UISystem
{
    // Every settings group the registry knows, plus the active keybinds, as one screen. The shell is
    // Settings.ui.xml — frame, title bar and star sizing come from there; only the rows are reflected,
    // because a new setting has to appear without anyone authoring it. Built per open and closed by
    // its own title bar, the same as a torn-off tab window.
    public static class SettingsWindow
    {
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("Settings");

        private const string windowName = "settings";
        private const string document = "settings";
        private const uint windowWidth = 720;
        private const uint windowHeight = 480;
        private const string keybindsCategory = "Keybinds";

        // layout
        private const int rowHeight = 26;
        private const int labelWidth = 230;
        private const int editorWidth = 220;
        private const int categoryWidth = 144;

        // palette, matching the app chrome
        private const string labelHex = "#CCCCCC";
        private const string fieldGroundHex = "#1B1B1B";
        private const string buttonHex = "#3A3A3A";
        private const string buttonHoverHex = "#4A4A4A";
        private const string buttonPressHex = "#2A2A2A";

        private static StackPanelControl _rows = null!;

        public static unsafe void Open(RenderWindow source)
        {
            if (source == null) return;

            // One screen at a time; a second ask raises the one already up.
            if (Engine.windows.TryGetValue(windowName, out RenderWindow existing))
            {
                existing.os.Show();
                existing.os.Focus();
                return;
            }

            RenderWindow window = Engine.OpenMenuWindow(windowName, windowWidth, windowHeight, true);
            window.isActivable = true;

            WindowControl root = (WindowControl)VulkanControl.ParseXML(document);
            window.ui.uiRoot = root;

            StackPanelControl categories = (StackPanelControl)root.FindByName("Categories");
            _rows = (StackPanelControl)root.FindByName("Rows");

            foreach (string category in Categories())
            {
                string named = category;
                ButtonControl button = Button(named, () => ShowCategory(named), categoryWidth);
                button.horizontalPosition = 0f;
                categories.AddChild(button);
            }

            window.os.Resize(windowWidth, windowHeight);
            root.FitTo(new Extent2D(windowWidth, windowHeight));

            ShowCategory(FirstCategory());

            AGlfwWindow._glfw.GetWindowPos(source.os.handle, out int sx, out int sy);
            AGlfwWindow._glfw.GetWindowSize(source.os.handle, out int sw, out int sh);
            window.os.SetPosition(sx + (sw - (int)windowWidth) / 2, sy + (sh - (int)windowHeight) / 2);

            window.os.Show();
            window.os.Focus();
            window.os.SeedIsInWindow();
        }

        [A_XSDActionDependency("Settings.Open", "UI", "Opens the settings screen over the window that asked")]
        public static void Open() => Open(UIActions.Invoking());

        // Saving is what applies — the rows already hold the new values, so this is where OnChanged
        // fires and the user's file is written.
        [A_XSDActionDependency("Settings.Save", "UI", "Applies and writes the settings screen")]
        public static void Save() => SettingsRegistry.Commit();

        #region ---- rows ----
        private static IEnumerable<string> Categories()
        {
            foreach (ISettingsGroup group in SettingsRegistry.Groups.Values)
                if (group is SettingCategory)
                    yield return NameOf(group.GetType());

            yield return keybindsCategory;
        }

        private static string FirstCategory() => Categories().First();

        private static string NameOf(Type type) =>
            type.GetCustomAttribute<A_XSDTypeAttribute>(false)?.Name ?? type.Name;

        private static void ShowCategory(string category)
        {
            foreach (Entity child in _rows.children.ToArray())
                child.Destroy();

            if (category == keybindsCategory) FillKeybinds();
            else FillSettings(category);

            _rows.InvalidateLayout();
        }

        // An App-scoped setting is the build's decision, so it is not shown at all rather than shown
        // and refused.
        private static void FillSettings(string category)
        {
            foreach (ISettingsGroup group in SettingsRegistry.Groups.Values)
            {
                if (group is not SettingCategory settings || NameOf(group.GetType()) != category) continue;

                foreach (Setting setting in settings.settings)
                {
                    if (setting.scope == SettingScope.App) continue;

                    foreach (MemberInfo member in Setting.ValueMembers(setting.GetType()))
                        _rows.AddChild(Row(
                            $"{SettingCategory.NameOf(setting)} {member.GetCustomAttribute<A_XSDElementPropertyAttribute>().Name}",
                            Editor(setting, member)));
                }
            }
        }

        private static void FillKeybinds()
        {
            InputHandler handler = Engine.inputHandler;
            if (handler == null) return;

            foreach (KeybindDefinition bind in handler.gestureMatcher.ActiveBinds)
            {
                if (bind.actionName.Length == 0) continue;
                _rows.AddChild(Row(bind.actionName, KeybindEditor(bind)));
            }
        }

        private static VulkanControl Row(string caption, VulkanControl editor)
        {
            StackPanelControl row = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Horizontal,
                maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible"),
                preferredHeight = rowHeight,
                Spacing = 10
            };

            row.AddChild(new LabelControl
            {
                text = caption,
                fontSize = 14,
                controlColorHex = labelHex,
                preferredWidth = labelWidth,
                horizontalPosition = 0f
            });
            row.AddChild(editor);

            return row;
        }
        #endregion

        #region ---- editors ----
        // The editor a member's type asks for. An enum restricted by [A_XSDDomain] offers the
        // domain's variants, which is the same set the loader would accept.
        private static VulkanControl Editor(Setting setting, MemberInfo member)
        {
            Type memberType = XmlReflection.MemberType(member);
            object current = XmlReflection.GetMember(member, setting);

            if (memberType == typeof(bool))
            {
                CheckBoxControl box = new CheckBoxControl
                {
                    controlColorHex = fieldGroundHex,
                    hoverColorHex = buttonHoverHex,
                    isChecked = (bool)current
                };
                box.onChanged = value => XmlReflection.SetMember(member, setting, value);
                return box;
            }

            if (memberType.IsEnum)
            {
                Type domain = A_XSDDomainAttribute.DomainOf(member) ?? memberType;

                DropdownControl dropdown = new DropdownControl
                {
                    preferredWidth = editorWidth,
                    preferredHeight = rowHeight,
                    controlColorHex = fieldGroundHex,
                    hoverColorHex = buttonHoverHex,
                    pressColorHex = buttonPressHex,
                    cornerRadius = new VulkanControl.CornerRadii(3),
                    options = Enum.GetNames(domain),
                    selected = current?.ToString() ?? ""
                };
                dropdown.onPicked = value => XmlReflection.SetMember(member, setting, Enum.Parse(memberType, value));
                return dropdown;
            }

            TextBoxControl field = new TextBoxControl
            {
                preferredWidth = editorWidth,
                preferredHeight = rowHeight,
                controlColorHex = fieldGroundHex,
                textColorHex = "#EAEAEA",
                fontSize = 14,
                padding = new VulkanControl.Thickness(0, 6, 0, 8),
                text = current?.ToString() ?? ""
            };
            field.onCommit = text =>
            {
                try
                {
                    XmlReflection.SetMember(member, setting,
                        TypeDescriptor.GetConverter(memberType).ConvertFromInvariantString(text));
                }
                catch (Exception)
                {
                    Log.Warn($"'{text}' is not a valid {memberType.Name} — keeping {XmlReflection.GetMember(member, setting)}.");
                    field.text = XmlReflection.GetMember(member, setting)?.ToString() ?? "";
                }
            };
            return field;
        }

        // A locked bind is the build's, so it reads as text with nothing to press.
        private static VulkanControl KeybindEditor(KeybindDefinition bind)
        {
            List<Keys> modifiers = bind.modifiers.Select(m => m.key).ToList();

            if (bind.access == KeybindAccess.Locked)
                return new LabelControl
                {
                    text = KeyCaptureControl.Describe(bind.trigger, modifiers),
                    fontSize = 14,
                    controlColorHex = "#6E6E6E",
                    preferredWidth = editorWidth,
                    horizontalPosition = 0f
                };

            KeyCaptureControl capture = new KeyCaptureControl
            {
                preferredWidth = editorWidth,
                preferredHeight = rowHeight,
                controlColorHex = fieldGroundHex,
                hoverColorHex = buttonHoverHex,
                pressColorHex = buttonPressHex,
                cornerRadius = new VulkanControl.CornerRadii(3)
            };
            capture.SetCombo(bind.trigger, modifiers);
            capture.onCaptured = (trigger, held) =>
            {
                List<KeybindModifier> bound = held.Select(k => new KeybindModifier { key = k }).ToList();
                if (!Engine.inputHandler.gestureMatcher.Rebind(bind.actionName, trigger, bound))
                {
                    capture.SetCombo(bind.trigger, bind.modifiers.Select(m => m.key));
                    return;
                }
                SettingsRegistry.Get<InputBindings>().Remember(bind.actionName, trigger, bound);
            };
            return capture;
        }

        private static ButtonControl Button(string caption, Action action, int width = 90)
        {
            ButtonControl button = new ButtonControl
            {
                preferredWidth = width,
                preferredHeight = 26,
                controlColorHex = buttonHex,
                hoverColorHex = buttonHoverHex,
                pressColorHex = buttonPressHex,
                cornerRadius = new VulkanControl.CornerRadii(4)
            };
            button.AddChild(new LabelControl { text = caption, fontSize = 14, controlColorHex = labelHex });
            button.RegisterOnRelease(action);
            return button;
        }
        #endregion
    }
}
