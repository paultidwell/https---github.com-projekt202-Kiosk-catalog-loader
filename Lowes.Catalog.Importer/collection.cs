//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Lowes.Catalog.Importer
{
    using System;
    using System.Collections.Generic;
    
    public partial class collection
    {
        public collection()
        {
            this.collections_groups = new HashSet<collections_groups>();
        }
    
        public int id { get; set; }
        public string name { get; set; }
        public string roomType { get; set; }
        public string imageUrl { get; set; }
    
        public virtual roomtype roomtype1 { get; set; }
        public virtual ICollection<collections_groups> collections_groups { get; set; }
    }
}