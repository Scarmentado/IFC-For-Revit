﻿//
// BIM IFC library: this library works with Autodesk(R) Revit(R) to export IFC files containing model geometry.
// Copyright (C) 2012  Autodesk, Inc.
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Autodesk.Revit.DB.Structure;
using BIM.IFC.Utility;

namespace BIM.IFC.Exporter.PropertySet.Calculators
{
    /// <summary>
    /// A calculation class to calculate the start hook angle for a rebar.
    /// </summary>
    class ISOCD3766BendingStartHookCalculator : PropertyCalculator
    {
        /// <summary>
        /// A double variable to keep the calculated value.
        /// </summary>
        private double m_Angle = 0;

        /// <summary>
        /// A static instance of this class.
        /// </summary>
        static ISOCD3766BendingStartHookCalculator s_Instance = new ISOCD3766BendingStartHookCalculator();

        /// <summary>
        /// The ISOCD3766BendingStartHookCalculator instance.
        /// </summary>
        public static ISOCD3766BendingStartHookCalculator Instance
        {
            get { return s_Instance; }
        }

        /// <summary>
        /// Calculates the start hook angle for a rebar.
        /// </summary>
        /// <param name="exporterIFC">The ExporterIFC object.</param>
        /// <param name="extrusionCreationData">The IFCExtrusionCreationData.</param>
        /// <param name="element">The element to calculate the value.</param>
        /// <param name="elementType">The element type.</param>
        /// <returns>
        /// True if the operation succeed, false otherwise.
        /// </returns>
        public override bool Calculate(ExporterIFC exporterIFC, IFCExtrusionCreationData extrusionCreationData, Element element, ElementType elementType)
        {
            RebarBendData bendData = null;
            if (element is Rebar)
                bendData = (element as Rebar).GetBendData();
            else if (element is RebarInSystem)
                bendData = (element as RebarInSystem).GetBendData();
            
            if (bendData != null)
            {
                if (bendData.HookLength0 > MathUtil.Eps())
                {
                    ElementId hookAtStartTypeId;
                    if (ParameterUtil.GetElementIdValueFromElement(element, BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE, out hookAtStartTypeId) != null)
                    {
                        RebarHookType rebarHookType = element.Document.GetElement(hookAtStartTypeId) as RebarHookType;
                        if (rebarHookType != null)
                        {
                            //HookAngle is measured in radians, so scale directly.
                            m_Angle = rebarHookType.HookAngle * 180 / Math.PI;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the calculated double value.
        /// </summary>
        /// <returns>
        /// The double value.
        /// </returns>
        public override double GetDoubleValue()
        {
            return m_Angle;
        }
    }
}
