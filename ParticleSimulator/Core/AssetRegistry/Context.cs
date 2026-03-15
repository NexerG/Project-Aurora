using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.Core.AssetRegistry
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class ActiveContextAttribute : Attribute
    {
        public string name;
        public ActiveContextAttribute(string name)
        {
            this.name = name;
        }
    }

    public class Context
    {

    }
}