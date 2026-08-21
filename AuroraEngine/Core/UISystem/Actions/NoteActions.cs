using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

namespace ArctisAurora.Core.UISystem.Actions
{
    // Settling open notes: naming the ones that have never been named, and writing the ones that were
    // edited. Shared by the shutdown sequence, which settles every window, and by closing a single
    // window, which settles only that one.
    public static class NoteActions
    {
        // Notes the user chose not to save. Remembered for the length of one shutdown attempt, or the
        // walk finds them again and never gets past them.
        private static readonly HashSet<DocumentEditorControl> discarded = new HashSet<DocumentEditorControl>();

        internal static void ForgetDiscarded() => discarded.Clear();

        #region ---- shutdown steps ----
        // One prompt per attempt: the answer re-enters the sequence, which finds the next unnamed
        // note, so they are named one at a time in the order they are found. Cancelling never
        // re-enters, which is what leaves the application running.
        [A_XSDActionDependency("Notes.SettleUnnamed", "Shutdown", "Asks for a name for every open note that has never had one")]
        public static bool SettleUnnamed()
        {
            DocumentEditorControl unnamed = FirstUnnamed();
            if (unnamed == null) return true;

            unnamed.SaveNamed(
                Shutdown.Resume,
                () => { discarded.Add(unnamed); Shutdown.Resume(); });

            return false;
        }

        [A_XSDActionDependency("Notes.SaveEdited", "Shutdown", "Writes every open note that was edited since its last save")]
        public static bool SaveEdited()
        {
            foreach (RenderWindow window in Engine.windows.Values)
                SaveEditedIn(window.ui.uiRoot);

            return true;
        }
        #endregion

        #region ---- one window ----
        // Settles a single window and then runs onSettled, for a window closing on its own rather
        // than the application going. Same one-prompt-at-a-time shape as the shutdown step.
        internal static void SettleWindow(RenderWindow window, Action onSettled)
        {
            DocumentEditorControl unnamed = FirstUnnamed(window.ui.uiRoot);
            if (unnamed != null)
            {
                unnamed.SaveNamed(
                    () => SettleWindow(window, onSettled),
                    () => { discarded.Add(unnamed); SettleWindow(window, onSettled); });
                return;
            }

            SaveEditedIn(window.ui.uiRoot);
            onSettled?.Invoke();
        }
        #endregion

        private static DocumentEditorControl FirstUnnamed()
        {
            foreach (RenderWindow window in Engine.windows.Values)
                if (FirstUnnamed(window.ui.uiRoot) is DocumentEditorControl found)
                    return found;

            return null;
        }

        private static DocumentEditorControl FirstUnnamed(VulkanControl control)
        {
            if (control == null) return null;
            if (control is DocumentEditorControl editor && editor.needsNaming && !discarded.Contains(editor))
                return editor;

            foreach (Entity child in control.children)
                if (child is VulkanControl childControl && FirstUnnamed(childControl) is DocumentEditorControl found)
                    return found;

            return null;
        }

        // Only what was actually edited. A first save of a hand-authored note does not reproduce its
        // bytes, so writing untouched ones would rewrite every open file on every close.
        private static void SaveEditedIn(VulkanControl control)
        {
            if (control == null) return;
            if (control is DocumentEditorControl editor && !discarded.Contains(editor)
                && editor.session != null && editor.session.isDirty)
                editor.Save();

            foreach (Entity child in control.children)
                if (child is VulkanControl childControl)
                    SaveEditedIn(childControl);
        }
    }
}
