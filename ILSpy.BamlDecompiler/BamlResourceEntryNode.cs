// Copyright (c) AlphaSierraPapa for the SharpDevelop Team
// This code is distributed under the MS-PL (for details please see \doc\MS-PL.txt)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.ILSpy;
using ICSharpCode.ILSpy.TextView;
using ICSharpCode.ILSpy.TreeNodes;
using Mono.Cecil;
using Ricciolo.StylesExplorer.MarkupReflection;

namespace ILSpy.BamlDecompiler
{
	public sealed class BamlResourceEntryNode : ResourceEntryNode
	{
		public BamlResourceEntryNode(string key, Stream data) : base(key, data)
		{
		}
		
		public override bool View(DecompilerTextView textView)
		{
			IHighlightingDefinition highlighting = null;
			
			textView.RunWithCancellation(
				token => Task.Factory.StartNew(
					() => {
						AvalonEditTextOutput output = new AvalonEditTextOutput();
						try {
							if (LoadBaml(output))
								highlighting = HighlightingManager.Instance.GetDefinitionByExtension(".xml");
						} catch (Exception ex) {
							output.Write(ex.ToString());
						}
						return output;
					}, token))
				.Then(output => textView.ShowNode(output, this, highlighting))
				.HandleExceptions();
			return true;
		}
		
		bool LoadBaml(AvalonEditTextOutput output)
		{
			var asm = this.Ancestors().OfType<AssemblyTreeNode>().FirstOrDefault().LoadedAssembly;
			Data.Position = 0;
			XDocument xamlDocument = BamlDecompiler.LoadIntoDocument(asm.GetAssemblyResolver(), asm.AssemblyDefinition, Data);
			output.Write(xamlDocument.ToString());
			return true;
		}
	}
}