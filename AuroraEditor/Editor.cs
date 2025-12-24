using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering.Modules;
using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.Rendering.UI.Controls;

namespace AuroraEditor
{
    internal class Editor
    {
        static void Main(string[] args)
        {
            VulkanUIHandler.GenerateTestXSD();

            Engine engine = new Engine();
            RenderingModule[] modules = new RenderingModule[]
            {
                new UIModule(),
            };

            engine.Init(modules, false);

            // SERIALIZATION TESTING
            //AuroraScene scene = new AuroraScene();
            //TestingEntity e = new TestingEntity();
            //TestingEntity e2 = new TestingEntity();
            //e.children.Add(e2);
            //scene.entities.Add(e);
            //
            //AuroraScene.SaveScene(scene);
            //
            //AuroraScene newS = new AuroraScene();
            //string path = Paths.SCENES + "\\NewScene.as";
            //Serializer.Deserialize(path, ref newS);

            // prepare level
            WindowControl windowControl = VulkanUIHandler.ParseXML("UITest.xml");

            //ButtonControl control = new();
            //control.transform.SetWorldPosition(new Vector3D<float>(300, 300, -10));
            //control.transform.SetWorldScale(new Vector3D<float>(200, 100, 1));
            //
            //EntityManager.uiTree = control;

            //ButtonControl btn = new ButtonControl();
            //btn.RegisterOnClick(Decorations.ExitApplication);
            //btn.RegisterOnEnter(Decorations.DummyHover);
            //btn.transform.SetWorldPosition(new Vector3D<float>(300, 300, -10));
            //btn.transform.SetWorldScale(new Vector3D<float>(200, 100, 1));
            //EntityManager.uiTree = btn;

            //TextEntity _te = new TextEntity("A", 70, new Vector3D<float>(1, 100, 100));
            //TextEntity _te2 = new TextEntity("A", 70, new Vector3D<float>(1, 200, 200));
            //PanelControl control = new PanelControl();
            //ResizeableControl control = new();
            //control.transform.SetWorldPosition(new Vector3D<float>(213, 360, -10));
            //control.transform.SetWorldScale(new Vector3D<float>(854, 720, 1));
            //ButtonControl control2 = new();
            //ButtonControl control3 = new();
            //ButtonControl control4 = new();
            //ResizeableControl control5 = new();
            //control2.controlData.style.tintDefault = new Vector3D<float>(0.8f, 0.1f, 0.1f);
            //control3.controlData.style.tintDefault = new Vector3D<float>(0.1f, 0.8f, 0.1f);
            //control4.controlData.style.tintDefault = new Vector3D<float>(0.1f, 0.1f, 0.8f);
            //control5.controlData.style.tintDefault = new Vector3D<float>(0.8f, 0.8f, 0.1f);
            //control2.UpdateControlData();
            //control.RegisterOnEnter(TestEnter);
            //control.RegisterOnExit(TestExit);
            //control.RegisterOnClick(TestClick);
            //control.RegisterOnRelease(TestRelease);
            //control.RegisterAltClick(TestAltClick);
            //control.RegisterAltRelease(TestAltRelease);
            //control.RegisterDoubleClick(TestDoubleClick);
            //control.transform.SetWorldPosition(new Vector3D<float>(1.1f, 200, 200));

            //DockingControl dock = new DockingControl(null);
            //dock.Dock(control, DockMode.left);
            //dock.Dock(control2, DockMode.left);
            //dock.Dock(control3, DockMode.top);
            //dock.Dock(control4, DockMode.right);
            //dock.Dock(control5, DockMode.fill);
            //uiCollisionHandler.defaultContextMenu = new ContextMenuControl();

            engine.Run();   
        }
    }
}
