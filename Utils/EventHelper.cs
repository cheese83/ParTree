using System;
using System.Collections.Generic;
using System.Linq;

namespace ParTree
{
    public class RelatedPropertiesAttribute : Attribute
    {
        public IReadOnlyList<string> Names { get; }

        public RelatedPropertiesAttribute(params string[] names)
        {
            Names = names.ToList();
        }
    }

    public class RelatedParentPropertiesAttribute : RelatedPropertiesAttribute
    {
        public RelatedParentPropertiesAttribute(params string[] names) : base(names) { }
    }
}
