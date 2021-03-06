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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Forms;
using System.IO;
using Revit.IFC.Common.Utility;
using System.Xml.Linq;
using RevitIFCTools.PropertySet;

namespace RevitIFCTools
{
   /// <summary>
   /// Interaction logic for GeneratePsetDefitions.xaml
   /// </summary>
   public partial class GeneratePsetDefWin : Window
   {
      string outputFilename = "";
      string sourceFolder = "";
      IDictionary<string, StreamWriter> enumFileDict;
      IDictionary<string, IList<string>> enumDict;
      SortedDictionary<string, IList<VersionSpecificPropertyDef>> allPDefDict;
#if DEBUG
      StreamWriter logF;
#endif

      private class VersionSpecificPropertyDef
      {
         public string SchemaFileVersion { get; set; }
         public string IfcVersion { get; set; }
         public PsetDefinition PropertySetDef { get; set; }
      }

      public GeneratePsetDefWin()
      {
         InitializeComponent();
         textBox_PSDSourceDir.Text = sourceFolder;
         textBox_OutputFile.Text = outputFilename;
         enumFileDict = new Dictionary<string, StreamWriter>();
         enumDict = new Dictionary<string, IList<string>>();
#if DEBUG
         string tempFolder = System.IO.Path.GetTempPath();
         logF = new StreamWriter(Path.Combine(tempFolder, "GeneratePsetDefWin.log"));
#endif
      }

      private void button_PSDSourceDir_Click(object sender, RoutedEventArgs e)
      {
         var dialog = new FolderBrowserDialog();

         dialog.ShowDialog();
         textBox_PSDSourceDir.Text = dialog.SelectedPath;
         if (string.IsNullOrEmpty(textBox_PSDSourceDir.Text))
            return;
      }

      private void button_OutputFile_Click(object sender, RoutedEventArgs e)
      {
         var dialog = new OpenFileDialog();
         dialog.DefaultExt = "cs";
         dialog.Filter = "Select *.cs output file|*.cs";
         dialog.AddExtension = true;
         dialog.CheckFileExists = false;
         dialog.ShowDialog();
         textBox_OutputFile.Text = dialog.FileName;
         outputFilename = textBox_OutputFile.Text;
      }

      private PsetDefinition ProcessPsetDef(string schemaVersion, FileInfo PSDfileName)
      {
         PsetDefinition pset = new PsetDefinition();
         XDocument doc = XDocument.Load(PSDfileName.FullName);
         
         // Older versions of psd uses namespace!
         var nsInfo = doc.Root.Attributes("xmlns").FirstOrDefault();
         XNamespace ns = "";
         if (nsInfo != null)
            ns = nsInfo.Value;

         pset.Name = doc.Elements(ns + "PropertySetDef").Elements(ns + "Name").FirstOrDefault().Value;
         pset.IfcVersion = doc.Elements(ns + "PropertySetDef").Elements(ns + "IfcVersion").FirstOrDefault().Attribute("version").Value.Replace(" ","");
         if (pset.IfcVersion.StartsWith("2"))
         {
            if (pset.IfcVersion.Equals("2X", StringComparison.CurrentCultureIgnoreCase))
               pset.IfcVersion = "IFC" + pset.IfcVersion.ToUpper() + "2";  // BUG in the documentation. It ony contains "2x" instead of "2x2"
            else
               pset.IfcVersion = "IFC" + pset.IfcVersion.ToUpper();   // Namespace cannot start with a number. e.g. make sure 2x3 -> IFC2x3
         }
         else if (pset.IfcVersion.StartsWith("IFC4"))
            pset.IfcVersion = schemaVersion.ToUpper();

         if (doc.Element(ns + "PropertySetDef").Attribute("ifdguid") != null)
            pset.IfdGuid = doc.Element(ns + "PropertySetDef").Attribute("ifdguid").Value;
         // Get applicable classes
         IEnumerable<XElement> applicableClasses = from el in doc.Descendants(ns+"ClassName") select el;
         IList<string> applClassesList = new List<string>();
         foreach (XElement applClass in applicableClasses)
            applClassesList.Add(removeInvalidNName(applClass.Value));
         pset.ApplicableClasses = applClassesList;

         IList<PsetProperty> propList = new List<PsetProperty>();
         var pDefs = from p in doc.Descendants(ns + "PropertyDef") select p;
         foreach (XElement pDef in pDefs)
         {
            PsetProperty prop = getPropertyDef(ns, pDef);
            if (prop==null)
            {
#if DEBUG
               logF.WriteLine("%Error: Mising PropertyType data for {0}.{1}", pset.Name, pDef.Element(ns+ "Name").Value);
#endif
            }
            else
               propList.Add(prop);
         }
         pset.properties = propList;

         return pset;
      }

      private PsetProperty getPropertyDef (XNamespace ns, XElement pDef)
      {
         PsetProperty prop = new PsetProperty();
         if (pDef.Attribute("ifdguid") != null)
            prop.IfdGuid = pDef.Attribute("ifdguid").Value;
         prop.Name = pDef.Element(ns + "Name").Value;
         IList<NameAlias> aliases = new List<NameAlias>();
         XElement nAliasesElem = pDef.Elements(ns + "NameAliases").FirstOrDefault();
         if (nAliasesElem != null)
         {
            var nAliases = from el in nAliasesElem.Elements(ns + "NameAlias") select el;
            foreach (XElement alias in nAliases)
            {
               NameAlias nameAlias = new NameAlias();
               nameAlias.Alias = alias.Value;
               nameAlias.lang = alias.Attribute("lang").Value;
               aliases.Add(nameAlias);
            }
         }
         if (aliases.Count > 0)
            prop.NameAliases = aliases;

         PropertyDataType dataTyp = null;
         var propType = pDef.Elements(ns + "PropertyType").FirstOrDefault();
         XElement propDetType = propType.Elements().FirstOrDefault();
         if (propDetType == null)
         {
#if DEBUG
            logF.WriteLine("%Warning: Missing PropertyType for {0}.{1}", pDef.Parent.Parent.Element(ns + "Name").Value, prop.Name);
#endif
            return prop;
         }

         if (propDetType.Name.LocalName.Equals("TypePropertySingleValue"))
         {
            XElement dataType = propDetType.Element(ns + "DataType");
            PropertySingleValue sv = new PropertySingleValue();
            if (dataType.Attribute("type") != null)
            {
               sv.DataType = dataType.Attribute("type").Value;
            }
            else
            {
               sv.DataType = "IfcLabel";     // Set this to default if missing
#if DEBUG
               logF.WriteLine("%Warning: Missing TypePropertySingleValue for {0}.{1}", pDef.Parent.Parent.Element(ns + "Name").Value, prop.Name);
#endif
            }
            dataTyp = sv;
         }
         else if (propDetType.Name.LocalName.Equals("TypePropertyReferenceValue"))
         {
            PropertyReferenceValue rv = new PropertyReferenceValue();
            // Older versions uses Element DataType!
            XElement dt = propDetType.Element(ns + "DataType");
            if (dt == null)
            {
               rv.RefEntity = propDetType.Attribute("reftype").Value;
            }
            else
            {
               rv.RefEntity = dt.Attribute("type").Value;
            }
            dataTyp = rv;
         }
         else if (propDetType.Name.LocalName.Equals("TypePropertyEnumeratedValue"))
         {
            PropertyEnumeratedValue pev = new PropertyEnumeratedValue();
            var enumItems = propDetType.Descendants(ns + "EnumItem");
            if (enumItems.Count() > 0)
            {
               pev.Name = propDetType.Element(ns + "EnumList").Attribute("name").Value;
               pev.EnumDef = new List<PropertyEnumItem>();
               foreach (var en in enumItems)
               {
                  string enumItemName = en.Value.ToString();
                  IEnumerable<XElement> consDef = null;
                  if (propDetType.Element(ns + "ConstantList") != null)
                  {
                     consDef = from el in propDetType.Element(ns + "ConstantList").Elements(ns + "ConstantDef")
                               where (el.Element(ns + "Name").Value.Equals(enumItemName, StringComparison.CurrentCultureIgnoreCase))
                               select el;
                  }

                  if (propDetType.Element(ns + "ConstantList") != null)
                  {
                     var consList = propDetType.Element(ns + "ConstantList").Elements(ns + "ConstantDef");
                     if (consList != null && consList.Count() != enumItems.Count())
                     {
#if DEBUG
                        logF.WriteLine("%Warning: EnumList (" + enumItems.Count().ToString() + ") is not consistent with the ConstantList ("
                           + consList.Count().ToString() + ") for: {0}.{1}",
                           pDef.Parent.Parent.Element(ns + "Name").Value, prop.Name);
#endif
                     }
                  }

                  if (consDef != null && consDef.Count() > 0)
                  {
                     foreach (var cD in consDef)
                     {
                        PropertyEnumItem enumItem = new PropertyEnumItem();
                        enumItem.EnumItem = cD.Elements(ns + "Name").FirstOrDefault().Value;
                        enumItem.Aliases = new List<NameAlias>();
                        var eAliases = from el in cD.Elements(ns + "NameAliases").FirstOrDefault().Elements(ns + "NameAlias") select el;
                        if (eAliases.Count() > 0)
                        {
                           foreach (var aliasItem in eAliases)
                           {
                              NameAlias nal = new NameAlias();
                              nal.Alias = aliasItem.Value;
                              nal.lang = aliasItem.Attribute("lang").Value;
                              enumItem.Aliases.Add(nal);
                           }
                        }
                        pev.EnumDef.Add(enumItem);
                     }
                  }
                  else
                  {
                     PropertyEnumItem enumItem = new PropertyEnumItem();
                     enumItem.EnumItem = enumItemName;
                     enumItem.Aliases = new List<NameAlias>();
                     pev.EnumDef.Add(enumItem);
                  }
               }
            }
            else
            {
               {
#if DEBUG
                  logF.WriteLine("%Warning: EnumList {0}.{1} is empty!", pDef.Parent.Parent.Element(ns+"Name").Value, prop.Name);
#endif
               }
               // If EnumList is empty, try to see whether ConstantDef has values. The Enum item name will be taken from the ConstantDef.Name
               pev.Name = "PEnum_" + prop.Name;
               pev.EnumDef = new List<PropertyEnumItem>();
               var consDef = from el in propDetType.Element(ns + "ConstantList").Elements(ns + "ConstantDef")
                             select el;
               if (consDef != null && consDef.Count() > 0)
               {
                  foreach (var cD in consDef)
                  {
                     PropertyEnumItem enumItem = new PropertyEnumItem();
                     enumItem.EnumItem = cD.Elements(ns + "Name").FirstOrDefault().Value;
                     //pev.Name = enumItem.EnumItem;             // Use this constant def for the missing Enum item
                     enumItem.Aliases = new List<NameAlias>();
                     var eAliases = from el in cD.Elements(ns + "NameAliases").FirstOrDefault().Elements(ns + "NameAlias") select el;
                     if (eAliases.Count() > 0)
                        foreach (var aliasItem in eAliases)
                        {
                           NameAlias nal = new NameAlias();
                           nal.Alias = aliasItem.Value;
                           nal.lang = aliasItem.Attribute("lang").Value;
                           enumItem.Aliases.Add(nal);
                        }
                     pev.EnumDef.Add(enumItem);
                  }
               }
            }
            dataTyp = pev;
         }
         else if (propDetType.Name.LocalName.Equals("TypePropertyBoundedValue"))
         {
            XElement dataType = propDetType.Element(ns + "DataType");
            PropertyBoundedValue bv = new PropertyBoundedValue();
            bv.DataType = dataType.Attribute("type").Value;
            dataTyp = bv;
         }
         else if (propDetType.Name.LocalName.Equals("TypePropertyListValue"))
         {
            XElement dataType = propDetType.Descendants(ns + "DataType").FirstOrDefault();
            PropertyListValue lv = new PropertyListValue();
            lv.DataType = dataType.Attribute("type").Value;
            dataTyp = lv;
         }
         else if (propDetType.Name.LocalName.Equals("TypePropertyTableValue"))
         {
            PropertyTableValue tv = new PropertyTableValue();
            var tve = propDetType.Element(ns + "Expression");
            if (tve != null)
               tv.Expression = tve.Value;
            XElement el = propDetType.Element(ns + "DefiningValue");
            if (el != null)
            {
               XElement el2 = propDetType.Element(ns + "DefiningValue").Element(ns + "DataType");
               if (el2 != null)
                  tv.DefiningValueType = el2.Attribute("type").Value;
            }
            el = propDetType.Element(ns + "DefinedValue");
            if (el != null)
            {
               XElement el2 = propDetType.Element(ns + "DefinedValue").Element(ns + "DataType");
               if (el2 != null)
                  tv.DefinedValueType = el2.Attribute("type").Value;
            }
            dataTyp = tv;
         }
         else if (propDetType.Name.LocalName.Equals("TypeComplexProperty"))
         {
            ComplexProperty compProp = new ComplexProperty();
            compProp.Name = propDetType.Attribute("name").Value;
            compProp.Properties = new List<PsetProperty>();
            foreach (XElement cpPropDef in propDetType.Elements(ns + "PropertyDef"))
            {
               PsetProperty pr = getPropertyDef(ns, cpPropDef);
               if (pr == null)
               {
#if DEBUG
                  logF.WriteLine("%Error: Mising PropertyType data in complex property {0}.{1}.{2}", propDetType.Parent.Parent.Element(ns + "Name").Value,
                     prop.Name, cpPropDef.Element(ns + "Name").Value);
#endif
               }
               else
                  compProp.Properties.Add(pr);
            }
            dataTyp = compProp;
         }
         prop.PropertyType = dataTyp;

         return prop;
      }

      private void button_Go_Click(object sender, RoutedEventArgs e)
      {

         textBox_OutputMsg.Clear();

         allPDefDict = new SortedDictionary<string, IList<VersionSpecificPropertyDef>>();

         if (string.IsNullOrEmpty(textBox_PSDSourceDir.Text) || string.IsNullOrEmpty(textBox_OutputFile.Text))
            return;

         var psdFolders = new DirectoryInfo(textBox_PSDSourceDir.Text).GetDirectories("psd", SearchOption.AllDirectories);
         //DirectoryInfo sourceDirInfo = new DirectoryInfo(textBox_PSDSourceDir.Text);

         // The output file is to generate codes to define property set and the propsety entries for Revit Exporter
         // We will also generate shared parameter files for a newer approach to deal with IFC propserty sets by using custom parameters
         //   under category of "IFC Parameters" group in form of:
         //     <Pset_name>.<property_name>
         // 

         string dirName = Path.GetDirectoryName(textBox_OutputFile.Text);
         string penumFileName = Path.GetFileNameWithoutExtension(textBox_OutputFile.Text);

         if (File.Exists(textBox_OutputFile.Text))
            File.Delete(textBox_OutputFile.Text);

         StreamWriter outF = new StreamWriter(textBox_OutputFile.Text);
         outF.WriteLine("/********************************************************************************************************************************");
         outF.WriteLine("** NOTE: This code is generated from IFC psd files automatically by RevitIFCTools.                                            **");
         outF.WriteLine("**       DO NOT change it manually as it will be overwritten the next time this file is re-generated!!                        **");
         outF.WriteLine("********************************************************************************************************************************/");
         outF.WriteLine();
         outF.WriteLine("using System;");
         outF.WriteLine("using System.Collections.Generic;");
         outF.WriteLine("using System.Linq;");
         outF.WriteLine("using System.Text;");
         outF.WriteLine("using System.Threading.Tasks;");
         outF.WriteLine("using Autodesk.Revit;");
         outF.WriteLine("using Autodesk.Revit.DB;");
         outF.WriteLine("using Autodesk.Revit.DB.IFC;");
         outF.WriteLine("using Autodesk.Revit.ApplicationServices;");
         outF.WriteLine("using Revit.IFC.Export.Exporter.PropertySet;");
         outF.WriteLine("using Revit.IFC.Export.Exporter.PropertySet.Calculators;");
         outF.WriteLine("using Revit.IFC.Export.Utility;");
         outF.WriteLine("using Revit.IFC.Export.Toolkit;");
         outF.WriteLine("using Revit.IFC.Common.Enums;");
         outF.WriteLine("");
         outF.WriteLine("namespace Revit.IFC.Export.Exporter");
         outF.WriteLine("{");
         outF.WriteLine("\tpartial class ExporterInitializer");
         outF.WriteLine("\t{");

         // Collect all Pset definition for psd folders
         foreach (DirectoryInfo psd in psdFolders)
         {
            DirectoryInfo schemaFolderPath = Directory.GetParent(psd.FullName);
            string schemaFolder = schemaFolderPath.Name;

#if DEBUG
            logF.WriteLine("\n*** Processing " + schemaFolder);
#endif
            foreach (DirectoryInfo subDir in psd.GetDirectories())
            {
               foreach (FileInfo file in subDir.GetFiles("Pset_*.xml"))
               {
#if DEBUG
                  logF.WriteLine("\n=== Processing " + file.Name);
#endif
                  textBox_OutputMsg.AppendText("Processing " + file.Name + "\n");
                  textBox_OutputMsg.ScrollToEnd();
                  PsetDefinition psetD = ProcessPsetDef(schemaFolder, file);
                  AddPsetDefToDict(schemaFolder, psetD);
               }
            }
            foreach (FileInfo file in psd.GetFiles("Pset_*.xml"))
            {
#if DEBUG
               logF.WriteLine("\n=== Processing " + file.Name);
#endif
               textBox_OutputMsg.AppendText("Processing " + file.Name + "\n");
               textBox_OutputMsg.ScrollToEnd();
               PsetDefinition psetD = ProcessPsetDef(schemaFolder, file);
               AddPsetDefToDict(schemaFolder, psetD);
            }
         }

         // For testing purpose: Dump all the propertyset definition in a text file
         if (checkBox_Dump.IsChecked.HasValue && checkBox_Dump.IsChecked.Value)
         {
            string pSetDump = "";
            foreach (KeyValuePair<string, IList<VersionSpecificPropertyDef>> psetDefEntry in allPDefDict)
            {
               pSetDump += "**** Property Set Name: " + psetDefEntry.Key;
               foreach (VersionSpecificPropertyDef vPdef in psetDefEntry.Value)
               {
                  pSetDump += "\n  ===> IfcVersion: " + vPdef.IfcVersion;
                  pSetDump += "\n" + vPdef.PropertySetDef.ToString() + "\n";
               }
               pSetDump += "\n\n";
            }
            string dumpDir = Path.GetDirectoryName(textBox_OutputFile.Text);
            string dumpFile = Path.GetFileNameWithoutExtension(textBox_OutputFile.Text) + ".txt";
            string dumpFilePath = Path.Combine(dumpDir, dumpFile);

            if (File.Exists(dumpFilePath))
               File.Delete(dumpFilePath);

            StreamWriter tx = new StreamWriter(dumpFilePath);
            tx.Write(pSetDump);
            tx.Close();
         }

         // Method to initialize all the prosertysets
         outF.WriteLine("\t\tpublic static void InitCommonPropertySets(IList<IList<PropertySetDescription>> propertySets)");
         outF.WriteLine("\t\t{");
         outF.WriteLine("\t\t\tIList<PropertySetDescription> commonPropertySets = new List<PropertySetDescription>();");
         foreach (KeyValuePair<string, IList<VersionSpecificPropertyDef>> psetDefEntry in allPDefDict)
         {
            outF.WriteLine("\t\t\tInit" + psetDefEntry.Key + "(commonPropertySets);");
         }
         outF.WriteLine("\n\t\t\tpropertySets.Add(commonPropertySets);");
         outF.WriteLine("\t\t}");
         outF.WriteLine("");

         // For generated codes and shared parameters
         foreach (KeyValuePair<string, IList<VersionSpecificPropertyDef>> psetDefEntry in allPDefDict)
         {
            string psetName = psetDefEntry.Key;
            outF.WriteLine("\t\tprivate static void Init" + psetName + "(IList<PropertySetDescription> commonPropertySets)");
            outF.WriteLine("\t\t{");

            string varName = psetDefEntry.Key.Replace("Pset_", "propertySet");

            outF.WriteLine("\t\t\tPropertySetDescription {0} = new PropertySetDescription();", varName);
            outF.WriteLine("\t\t\t{0}.Name = \"{1}\";", varName, psetName);

            string psetEnumStr = psetName.Replace("PSet_", "PSet");
            try
            {
               Revit.IFC.Export.Toolkit.IFCCommonPSets psetEnum = (Revit.IFC.Export.Toolkit.IFCCommonPSets)Enum.Parse(typeof(Revit.IFC.Export.Toolkit.IFCCommonPSets), psetEnumStr);
               outF.WriteLine("\t\t\t{0}.SubElementIndex = (int)IFCCommonPSets.{1};", varName, psetName.Replace("PSet_", "PSet"));
            }
            catch(ArgumentException)
            {
#if DEBUG
               logF.WriteLine("\t%Info: " + psetEnumStr + " is not defined in Revit.IFC.Export.Toolkit.IFCCommonPSets.");
#endif
            }

            outF.WriteLine("\t\t\tPropertySetEntry ifcPSE = null;");
            outF.WriteLine("\t\t\tType calcType = null;");

            foreach (VersionSpecificPropertyDef vspecPDef in psetDefEntry.Value)
            {
               PsetDefinition pDef = vspecPDef.PropertySetDef;

               if (vspecPDef.IfcVersion.Equals("IFC2X2", StringComparison.CurrentCultureIgnoreCase))
               {
                  outF.WriteLine("\t\t\tif (ExporterCacheManager.ExportOptionsCache.ExportAs2x2 && certifiedPsetList.AllowPsetToBeCreated(ExporterCacheManager.ExportOptionsCache.FileVersion.ToString().ToUpper(), \"" + psetName + "\"))");
                  outF.WriteLine("\t\t\t{");
                  foreach (string applEnt in vspecPDef.PropertySetDef.ApplicableClasses)
                  {
                     string applEnt2 = applEnt;
                     if (string.IsNullOrEmpty(applEnt))
                        applEnt2 = "IfcBuildingElementProxy";     // Default if somehow the data is empty
                     outF.WriteLine("\t\t\t\t{0}.EntityTypes.Add(IFCEntityType.{1});", varName, applEnt2);
                  }
               }
               else if (vspecPDef.IfcVersion.Equals("IFC2X3TC1", StringComparison.CurrentCultureIgnoreCase))
               {
                  outF.WriteLine("\t\t\tif (ExporterCacheManager.ExportOptionsCache.ExportAs2x3 && certifiedPsetList.AllowPsetToBeCreated(ExporterCacheManager.ExportOptionsCache.FileVersion.ToString().ToUpper(), \"" + psetName + "\"))");
                  outF.WriteLine("\t\t\t{");
                  foreach (string applEnt in vspecPDef.PropertySetDef.ApplicableClasses)
                  {
                     string applEnt2 = applEnt;
                     if (string.IsNullOrEmpty(applEnt))
                        applEnt2 = "IfcBuildingElementProxy";     // Default if somehow the data is empty
                     outF.WriteLine("\t\t\t\t{0}.EntityTypes.Add(IFCEntityType.{1});", varName, applEnt2);
                  }
               }
               //else if (vspecPDef.IfcVersion.Equals("IFC4_ADD1"))
               //{
                  else if (vspecPDef.SchemaFileVersion.Equals("IFC4_ADD1", StringComparison.CurrentCultureIgnoreCase))
                  {
                     outF.WriteLine("\t\t\tif (ExporterCacheManager.ExportOptionsCache.ExportAs4_ADD1 && certifiedPsetList.AllowPsetToBeCreated(ExporterCacheManager.ExportOptionsCache.FileVersion.ToString().ToUpper(), \"" + psetName + "\"))");
                     outF.WriteLine("\t\t\t{");
                     foreach (string applEnt in vspecPDef.PropertySetDef.ApplicableClasses)
                     {
                        string applEnt2 = applEnt;
                        if (string.IsNullOrEmpty(applEnt))
                           applEnt2 = "IfcBuildingElementProxy";     // Default if somehow the data is empty
                        outF.WriteLine("\t\t\t\t{0}.EntityTypes.Add(IFCEntityType.{1});", varName, applEnt2);
                     }
                  }
                  else if (vspecPDef.SchemaFileVersion.Equals("IFC4_ADD2", StringComparison.CurrentCultureIgnoreCase))
                  {
                     outF.WriteLine("\t\t\tif (ExporterCacheManager.ExportOptionsCache.ExportAs4_ADD2 && certifiedPsetList.AllowPsetToBeCreated(ExporterCacheManager.ExportOptionsCache.FileVersion.ToString().ToUpper(), \"" + psetName + "\"))");
                     outF.WriteLine("\t\t\t{");
                     foreach (string applEnt in vspecPDef.PropertySetDef.ApplicableClasses)
                     {
                        string applEnt2 = applEnt;
                        if (string.IsNullOrEmpty(applEnt))
                           applEnt2 = "IfcBuildingElementProxy";     // Default if somehow the data is empty
                        outF.WriteLine("\t\t\t\t{0}.EntityTypes.Add(IFCEntityType.{1});", varName, applEnt2);
                     }
                  }
               //}

               // Process each property
               foreach (PsetProperty prop in pDef.properties)
               {
                  // Handle only one level deep of complex property !!!!
                  if (prop.PropertyType is ComplexProperty)
                  {
                     ComplexProperty complexProp = prop.PropertyType as ComplexProperty;
                     // For complex property the properties will be flattened by using <Pset>.<Property>.<SubProperty>
                     foreach (PsetProperty propCx in complexProp.Properties)
                     {
                        string prefixName = pDef.Name + "." + prop.Name;
                        processSimpleProperty(outF, propCx, prefixName, pDef.IfcVersion, vspecPDef.SchemaFileVersion, varName);
                     }
                  }
                  else
                  {
                     processSimpleProperty(outF, prop, pDef.Name, pDef.IfcVersion, vspecPDef.SchemaFileVersion, varName);
                  }                    
               }
               outF.WriteLine("\t\t\t}");
            }
            outF.WriteLine("\t\t\tif (ifcPSE != null)");
            outF.WriteLine("\t\t\t{");
            //outF.WriteLine("\t\t\t\t{0}.Name = \"{1}\";", varName, psetName);
            outF.WriteLine("\t\t\t\tcommonPropertySets.Add({0});", varName);
            outF.WriteLine("\t\t\t}");
            outF.WriteLine("\t\t}");
            outF.WriteLine("\n");
         }

         outF.WriteLine("\t}");
         outF.WriteLine("}");
         outF.Close();
         endWriteEnumFile();
#if DEBUG
         logF.Close();
#endif
      }

      private void button_Cancel_Click(object sender, RoutedEventArgs e)
      {
         Close();
      }

      void AddPsetDefToDict(string schemaVersionName, PsetDefinition psetD)
      {
         VersionSpecificPropertyDef psetDefEntry = new VersionSpecificPropertyDef()
         {
            SchemaFileVersion = schemaVersionName,
            IfcVersion = psetD.IfcVersion,
            PropertySetDef = psetD
         };

         if (allPDefDict.ContainsKey(psetD.Name))
         {
            allPDefDict[psetD.Name].Add(psetDefEntry);
         }
         else
         {
            IList<VersionSpecificPropertyDef> vsPropDefList = new List<VersionSpecificPropertyDef>();
            vsPropDefList.Add(psetDefEntry);
            allPDefDict.Add(psetD.Name, vsPropDefList);
         }
      }

      LanguageType checkAliasLanguage(string language)
      {
         if (language.Equals("en-us", StringComparison.CurrentCultureIgnoreCase)
               || language.Equals("en", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.English_USA;

         if (language.Equals("ja-JP", StringComparison.CurrentCultureIgnoreCase)
               || language.Equals("ja", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Japanese;

         if (language.Equals("ko-KR", StringComparison.CurrentCultureIgnoreCase)
            || language.Equals("ko", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Korean;

         if (language.Equals("zh-CN", StringComparison.CurrentCultureIgnoreCase)
            || language.Equals("zh-SG", StringComparison.CurrentCultureIgnoreCase)
            || language.Equals("zh-HK", StringComparison.CurrentCultureIgnoreCase))
               return LanguageType.Chinese_Simplified;

         if (language.Equals("zh-TW", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Chinese_Traditional;

         if (language.Equals("fr-FR", StringComparison.CurrentCultureIgnoreCase)
            || language.Equals("fr", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.French;

         if (language.Equals("de-DE", StringComparison.CurrentCultureIgnoreCase)
            || language.Equals("de", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.German;

         if (language.Equals("es", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Spanish;

         if (language.Equals("it", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Italian;

         if (language.Equals("nl", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Dutch;

         if (language.Equals("ru", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Russian;

         if (language.Equals("cs", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Czech;

         if (language.Equals("pl", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Polish;

         if (language.Equals("hu", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Hungarian;

         if (language.Equals("pt-BR", StringComparison.CurrentCultureIgnoreCase))
            return LanguageType.Brazilian_Portuguese;

         return LanguageType.Unknown;
      }

      void writeEnumFile(string IfcVersion, string schemaVersion, string penumName, IList<string> enumValues)
      {
         if (string.IsNullOrEmpty(IfcVersion) || string.IsNullOrEmpty(penumName) || enumValues == null || enumValues.Count == 0)
            return;

         string version = IfcVersion;
         if (IfcVersion.Equals("IFC4", StringComparison.CurrentCultureIgnoreCase))
            version = schemaVersion.ToUpper();

         StreamWriter fileToWrite;
         if (!enumFileDict.TryGetValue(version, out fileToWrite))
         {
            string fileName = Path.Combine(Path.GetDirectoryName(textBox_OutputFile.Text),
                                 Path.GetFileNameWithoutExtension(textBox_OutputFile.Text) + version + "Enum.cs");
            if (File.Exists(fileName))
               File.Delete(fileName);
            fileToWrite = new StreamWriter(fileName);
            enumFileDict.Add(version, fileToWrite);

            fileToWrite.WriteLine("using System;");
            fileToWrite.WriteLine("using System.Collections.Generic;");
            fileToWrite.WriteLine("using System.Linq;");
            fileToWrite.WriteLine("using System.Text;");
            fileToWrite.WriteLine("using System.Threading.Tasks;");
            fileToWrite.WriteLine("using Autodesk.Revit;");
            fileToWrite.WriteLine("using Autodesk.Revit.DB;");
            fileToWrite.WriteLine("using Autodesk.Revit.DB.IFC;");
            fileToWrite.WriteLine("using Revit.IFC.Export.Exporter.PropertySet;");
            fileToWrite.WriteLine("using Revit.IFC.Export.Exporter.PropertySet.Calculators;");
            fileToWrite.WriteLine("using Revit.IFC.Export.Utility;");
            fileToWrite.WriteLine("using Revit.IFC.Export.Toolkit;");
            fileToWrite.WriteLine("using Revit.IFC.Common.Enums;");
            fileToWrite.WriteLine("");
            fileToWrite.WriteLine("namespace Revit.IFC.Export.Exporter.PropertySet." + version);
            fileToWrite.WriteLine("{");
            fileToWrite.WriteLine("");
         }

         // Check for duplicate Penum
         string key = version + "." + penumName;
         if (!enumDict.ContainsKey(key))
         {
            enumDict.Add(key, enumValues);
         }
         else
         {
            return;  // the enum already in the Dict, i.e. alreadey written, do not write it again
         }

         fileToWrite.WriteLine("");
         fileToWrite.WriteLine("\tpublic enum " + penumName + " {");
         foreach (string enumV in enumValues)
         {
            string endWith = ",";
            if (enumV == enumValues.Last())
               endWith = "}";

            fileToWrite.WriteLine("\t\t" + enumV + endWith);
         }
      }

      void endWriteEnumFile()
      {
         foreach (KeyValuePair<string, StreamWriter> enumFile in enumFileDict)
         {
            enumFile.Value.WriteLine("}");
            enumFile.Value.Close();
         }
      }

      string HandleInvalidCharacter(string name)
      {
         // 1. Check for all number or start with number enum name. Rename with _, and assign number as value for all number
         string appendValue = "";
         // If the name consists number only, assig the number value to the enum
         if (Regex.IsMatch(name, @"^\d+$"))
            appendValue = " = " + name;

         // if the enum starts with a number, add prefix to the enum with an underscore
         if (Regex.IsMatch(name, @"^\d"))
            name = "_" + name + appendValue;

         // check for any illegal character and remove them
         name = name.Replace(".", "").Replace("-", "");
         return name;
      }

      string removeInvalidNName(string name)
      {
         string[] subNames = name.Split('/');
         return subNames[0];  // Only returns the name before '/' if any
      }

      void processSimpleProperty(StreamWriter outF, PsetProperty prop, string propNamePrefix, string IfcVersion, string schemaVersion, string varName)
      {
         // For now, keep the same approach for naming the properties (i.e. without prefix)
         //outF.WriteLine("\t\t\t\tifcPSE = new PropertySetEntry(\"{0}.{1}\");", propNamePrefix, prop.Name);
         outF.WriteLine("\t\t\t\tifcPSE = new PropertySetEntry(\"{0}\");", prop.Name);
         outF.WriteLine("\t\t\t\tifcPSE.PropertyName = \"{0}\";", prop.Name);
         if (prop.PropertyType != null)
         {
            if (prop.PropertyType is PropertyEnumeratedValue)
            {
               PropertyEnumeratedValue propEnum = prop.PropertyType as PropertyEnumeratedValue;
               outF.WriteLine("\t\t\t\tifcPSE.PropertyType = PropertyType.Label;");
               outF.WriteLine("\t\t\t\tifcPSE.PropertyValueType = PropertyValueType.EnumeratedValue;");
               outF.WriteLine("\t\t\t\tifcPSE.PropertyEnumerationType = typeof(Revit.IFC.Export.Exporter.PropertySet." + IfcVersion + "." + propEnum.Name + ");");
               IList<string> enumItems = new List<string>();
               foreach (PropertyEnumItem enumItem in propEnum.EnumDef)
               {
                  string item = HandleInvalidCharacter(enumItem.EnumItem);
                  enumItems.Add(item);
               }
               writeEnumFile(IfcVersion, schemaVersion, propEnum.Name, enumItems);
            }
            else if (prop.PropertyType is PropertyReferenceValue)
            {
               PropertyReferenceValue propRef = prop.PropertyType as PropertyReferenceValue;
               outF.WriteLine("\t\t\t\tifcPSE.PropertyType = PropertyType.{0};", propRef.RefEntity.Trim());
               outF.WriteLine("\t\t\t\tifcPSE.PropertyValueType = PropertyValueType.ReferenceValue;");
            }
            else if (prop.PropertyType is PropertyListValue)
            {
               PropertyListValue propList = prop.PropertyType as PropertyListValue;
               if (propList.DataType != null)
                  outF.WriteLine("\t\t\t\tifcPSE.PropertyType = PropertyType.{0};", propList.DataType.ToString().Replace("Ifc", "").Replace("Measure", "").Trim());
               else
                  outF.WriteLine("\t\t\t\tifcPSE.PropertyType = PropertyType.Label;");    // default to Label if not defined

               outF.WriteLine("\t\t\t\tifcPSE.PropertyValueType = PropertyValueType.ListValue;");
            }
            else if (prop.PropertyType is PropertyTableValue)
            {
               PropertyTableValue propTab = prop.PropertyType as PropertyTableValue;
               // TableValue has 2 types: DefiningValue and DefinedValue. This is not fully implemented yet
               if (propTab.DefinedValueType != null)
                  outF.WriteLine("\t\t\t\tifcPSE.PropertyType = PropertyType.{0};", propTab.DefinedValueType.ToString().Replace("Ifc", "").Replace("Measure", "").Trim());
               else
                  outF.WriteLine("\t\t\t\tifcPSE.PropertyType = PropertyType.Label;");    // default to Label if missing

               outF.WriteLine("\t\t\t\tifcPSE.PropertyValueType = PropertyValueType.TableValue;");
            }
            else
               outF.WriteLine("\t\t\t\tifcPSE.PropertyType = PropertyType.{0};", prop.PropertyType.ToString().Replace("Ifc", "").Replace("Measure", "").Trim());
         }

         if (prop.NameAliases != null)
         {
            foreach (NameAlias alias in prop.NameAliases)
            {
               LanguageType lang = checkAliasLanguage(alias.lang);
               outF.WriteLine("\t\t\t\tifcPSE.AddLocalizedParameterName(LanguageType.{0}, \"{1}\");", lang, alias.Alias);
            }
         }

         string calcName = "Revit.IFC.Export.Exporter.PropertySet.Calculators." + prop.Name + "Calculator";
         outF.WriteLine("\t\t\t\tcalcType = System.Reflection.Assembly.GetExecutingAssembly().GetType(\"" + calcName + "\");");
         outF.WriteLine("\t\t\t\tif (calcType != null)");
         outF.WriteLine("\t\t\t\t\tifcPSE.PropertyCalculator = (PropertyCalculator) calcType.GetConstructor(Type.EmptyTypes).Invoke(new object[]{});");
         outF.WriteLine("\t\t\t\t{0}.AddEntry(ifcPSE);", varName);
         outF.WriteLine("");
      }
   }
}
