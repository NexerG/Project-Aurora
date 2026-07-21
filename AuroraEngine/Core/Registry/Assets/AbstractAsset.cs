using System;
using System.Collections.Generic;
using System.Text;

namespace ArctisAurora.Core.Registry.Assets
{
    public abstract class AbstractAsset
    {
        public abstract void LoadAsset(AbstractAsset asset, string name, string path);

        public abstract void LoadDefault();

        public abstract void LoadAll(string path);
    }
}
