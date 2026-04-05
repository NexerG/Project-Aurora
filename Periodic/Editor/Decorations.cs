using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork;

namespace Periodic.Editor
{
    public class Decorations
    {
        [A_XSDActionDependency("Write", category:"Input", "Writes the input to the active textbox")]
        public static void Write()
        {
            if (UICollisionHandling.activeControl == null) return;

            TextControl control = UICollisionHandling.activeControl as TextControl;
            if (control == null ) return;
            if (!control.isEditing) return;

            // if text box is editable
            Queue<char> inputChars = new Queue<char>(InputHandler.charInputReadQueue);
            while (inputChars.Count > 0)
            {
                control.WriteChar(inputChars.Dequeue());
            }
        }

        [A_XSDActionDependency("ExitApplication", category: "Input")]
        public static void ExitApplication()
        {
            Console.WriteLine("Exiting application...");
            Environment.Exit(0);
        }

        [A_XSDActionDependency("DummyHover", category: "Input")]
        public static void DummyHover()
        {
            Console.WriteLine("Hovering over button");
        }

        [A_XSDActionDependency("DummyKeyPress", category: "Input")]
        public static void DummyKeyPress()
        {
            Console.WriteLine($"Last character input was: {InputHandler.lastCharInput}");
        }

        [A_XSDActionDependency("ExitApplication2", category: "UI")]
        public static void ExitApplication2()
        {
            Environment.Exit(0);
        }
    }
}
