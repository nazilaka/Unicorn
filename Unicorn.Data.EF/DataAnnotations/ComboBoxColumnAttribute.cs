﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Unicorn.Data.EF.DataAnnotations
{
    public class ComboBoxColumnAttribute:Attribute
    {
        public Type ReferenceType { get; set; }
    }
}
