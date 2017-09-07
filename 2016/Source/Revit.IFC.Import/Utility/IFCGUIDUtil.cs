﻿//
// Revit IFC Import library: this library works with Autodesk(R) Revit(R) to import IFC files.
// Copyright (C) 2015  Autodesk, Inc.
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//

using Autodesk.Revit.DB;

namespace Revit.IFC.Import.Utility
{
    /// <summary>
    /// Utilities for GUID-related functions
    /// </summary>
    public class IFCGUIDUtil
    {
        /// <summary>
        /// Returns the IFC GUID associated with an element.
        /// </summary>
        /// <param name="elem">The element.</param>
        /// <returns>The string containing the GUID, or null if it doesn't exist.</returns>
        static public string GetGUID(Element elem)
        {
            BuiltInParameter paramId = (elem is ElementType) ? BuiltInParameter.IFC_TYPE_GUID : BuiltInParameter.IFC_GUID;
            Parameter param = elem.get_Parameter(paramId);
            if (param == null)
                return null;   // This Element was generated by other means.

            return param.AsString();
        }
    }
}
