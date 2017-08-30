// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using DiffLib;
using ICSharpCode.ILSpy;
using ICSharpCode.ILSpy.TextView;
using ICSharpCode.ILSpy.TreeNodes;
using Microsoft.Win32;
using Mono.Cecil;

namespace TestPlugin
{
	[ExportContextMenuEntryAttribute(Header = "Compare assemblies")]
	public class AssemblyApiCompare : IContextMenuEntry
	{
		[Import]
		DecompilerTextView decompilerTextView = null;

		public bool IsVisible(TextViewContext context)
		{
			return context.SelectedTreeNodes != null && context.SelectedTreeNodes.All(n => n is AssemblyTreeNode);
		}

		public bool IsEnabled(TextViewContext context)
		{
			return context.SelectedTreeNodes != null && context.SelectedTreeNodes.Length == 2;
		}

		public void Execute(TextViewContext context)
		{
			if (context.SelectedTreeNodes == null || context.SelectedTreeNodes.Length != 2)
				return;
			var leftNode = (AssemblyTreeNode)context.SelectedTreeNodes[0];
			var rightNode = (AssemblyTreeNode)context.SelectedTreeNodes[1];
			ShowDiff(leftNode, rightNode, false, false);
		}

		void ShowDiff(AssemblyTreeNode leftNode, AssemblyTreeNode rightNode, bool printChangesOnly, bool dumpPrivateMembers)
		{
			var leftAsm = leftNode.LoadedAssembly.AssemblyDefinition;
			var rightAsm = rightNode.LoadedAssembly.AssemblyDefinition;

			var leftSignatureDump = DumpAssemblyContent(leftNode.Language, leftAsm, dumpPrivateMembers);
			var rightSignatureDump = DumpAssemblyContent(rightNode.Language, rightAsm, dumpPrivateMembers);

			var output = new AvalonEditTextOutput();

			output.Write($"Base version:\t{leftAsm.FullName}" + Environment.NewLine);
			output.Write($"Other version:\t{rightAsm.FullName}" + Environment.NewLine);

			output.AddButton(null, "Exchange versions", (sender, e) => ShowDiff(rightNode, leftNode, printChangesOnly, dumpPrivateMembers));
			output.Write("\t");
			output.AddButton(null, "Toggle Show Changes/All", (sender, e) => ShowDiff(leftNode, rightNode, !printChangesOnly, dumpPrivateMembers));
			output.Write("\t");
			output.AddButton(null, "Toggle Compare Public Only/All", (sender, e) => ShowDiff(leftNode, rightNode, printChangesOnly, !dumpPrivateMembers));

			output.WriteLine();

			output.Write(Diff(leftSignatureDump, rightSignatureDump, printChangesOnly));
			decompilerTextView.ShowText(output);
		}

		private static string Diff(IEnumerable<string> input1, IEnumerable<string> input2, bool printChangesOnly)
		{
			var diff = new StringWriter();
			CodeComparer.Compare(input1, input2, diff, printChangesOnly);
			return diff.ToString();
		}

		private IEnumerable<string> DumpAssemblyContent(Language language, AssemblyDefinition assembly, bool dumpPrivateMembers = false)
		{
			foreach (var type in assembly.MainModule.Types.Where(t => dumpPrivateMembers || t.IsPublicAPI()).OrderBy(t => t.FullName, NaturalStringComparer.Instance)) {
				yield return type.FormatTypeName();
				foreach (var child in GetChildren(language, 1, type, dumpPrivateMembers)) {
					yield return new string('\t', child.Level) + child.Item;
				}
			}
		}

		private IEnumerable<(int Level, string Item)> GetChildren(Language language, int level, TypeDefinition type, bool dumpPrivateMembers)
		{
			foreach (var nestedType in type.NestedTypes.Where(t => dumpPrivateMembers || t.IsPublicAPI()).OrderBy(t => t.FullName, NaturalStringComparer.Instance)) {
				yield return (level, nestedType.FormatTypeName());
				foreach (var child in GetChildren(language, level + 1, nestedType, dumpPrivateMembers))
					yield return child;
			}

			foreach (var field in type.Fields.Where(f => dumpPrivateMembers || f.IsPublicAPI()).OrderBy(f => f.Name, NaturalStringComparer.Instance)) {
				yield return (level, field.Name + " : " + language.TypeToString(field.FieldType, false, field));
			}

			foreach (var method in type.Methods.Where(m => dumpPrivateMembers || m.IsPublicAPI()).OrderBy(m => m.Name, NaturalStringComparer.Instance)) {
				yield return (level, MethodTreeNode.GetText(method, language).ToString());
			}

			foreach (var property in type.Properties.Where(p => dumpPrivateMembers || p.IsPublicAPI()).OrderBy(p => p.Name, NaturalStringComparer.Instance)) {
				yield return (level, PropertyTreeNode.GetText(property, language).ToString());
			}

			foreach (var @event in type.Events.Where(e => dumpPrivateMembers || e.IsPublicAPI()).OrderBy(e => e.Name, NaturalStringComparer.Instance)) {
				yield return (level, EventTreeNode.GetText(@event, language).ToString());
			}
		}
	}

	public static class CodeComparer
	{
		public static void Compare(IEnumerable<string> input1, IEnumerable<string> input2, StringWriter diff, bool printChangesOnly)
		{
			var differ = new AlignedDiff<string>(
				input1,
				input2,
				StringComparer.Ordinal,
				new StringSimilarityComparer(),
				new StringAlignmentFilter());

			StringBuilder buffer = new StringBuilder();
			bool hasChanges = false;

			foreach (var change in differ.Generate()) {
				switch (change.Change) {
					case ChangeType.Same:
						if (printChangesOnly) {
							if (!change.Element1.StartsWith("\t")) {
								if (hasChanges) {
									diff.Write(buffer.ToString());
									hasChanges = false;
								}
								buffer.Clear();
								buffer.Append("    ");
								buffer.AppendLine(change.Element1);
							}
						} else {
							buffer.Append("    ");
							buffer.AppendLine(change.Element1);
						}
						break;
					case ChangeType.Added:
						if (printChangesOnly && !change.Element2.StartsWith("\t")) {
							buffer.Clear();
						}
						hasChanges = true;
						buffer.Append(" +  ");
						buffer.AppendLine(change.Element2);
						break;
					case ChangeType.Deleted:
						if (printChangesOnly && !change.Element1.StartsWith("\t")) {
							buffer.Clear();
						}
						hasChanges = true;
						buffer.Append(" -  ");
						buffer.AppendLine(change.Element1);
						break;
					case ChangeType.Changed:
						if (printChangesOnly && !change.Element1.StartsWith("\t")) {
							buffer.Clear();
						}
						hasChanges = true;
						buffer.Append("(-) ");
						buffer.AppendLine(change.Element1);
						buffer.Append("(+) ");
						buffer.AppendLine(change.Element2);
						break;
				}
			}
			if (!printChangesOnly || hasChanges) {
				diff.Write(buffer.ToString());
			}
		}
	}

	public static class CecilExtensions
	{
		public static bool IsPublicAPI(this TypeDefinition type)
		{
			if (type == null)
				throw new ArgumentNullException(nameof(type));
			switch (type.Attributes & TypeAttributes.VisibilityMask) {
				case TypeAttributes.Public:
				case TypeAttributes.NestedPublic:
				case TypeAttributes.NestedFamily:
				case TypeAttributes.NestedFamORAssem:
					return true;
				default:
					return false;
			}
		}

		public static string FormatTypeName(this TypeDefinition type)
		{
			string typeKind = "class";
			if (type.IsEnum)
				typeKind = "enum";
			else if (type.IsValueType)
				typeKind = "struct";
			else if (type.IsInterface)
				typeKind = "interface";
			else if (type.BaseType?.FullName == "System.Delegate")
				typeKind = "delegate";
			return $"{typeKind} {type.FullName}:";
		}

		public static bool IsPublicAPI(this FieldDefinition field)
		{
			if (field == null)
				throw new ArgumentNullException(nameof(field));
			return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
		}

		public static bool IsPublicAPI(this MethodDefinition method)
		{
			if (method == null)
				throw new ArgumentNullException(nameof(method));
			return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
		}

		public static bool IsPublicAPI(this PropertyDefinition property)
		{
			if (property == null)
				throw new ArgumentNullException(nameof(property));
			switch (GetAttributesOfMostAccessibleMethod(property) & MethodAttributes.MemberAccessMask) {
				case MethodAttributes.Public:
				case MethodAttributes.Family:
				case MethodAttributes.FamORAssem:
					return true;
				default:
					return false;
			}
		}

		public static bool IsPublicAPI(this EventDefinition @event)
		{
			if (@event == null)
				throw new ArgumentNullException(nameof(@event));
			MethodDefinition accessor = @event.AddMethod ?? @event.RemoveMethod;
			return accessor != null && (accessor.IsPublic || accessor.IsFamilyOrAssembly || accessor.IsFamily);
		}

		private static MethodAttributes GetAttributesOfMostAccessibleMethod(PropertyDefinition property)
		{
			// There should always be at least one method from which to
			// obtain the result, but the compiler doesn't know this so
			// initialize the result with a default value
			MethodAttributes result = (MethodAttributes)0;

			// Method access is defined from inaccessible (lowest) to public (highest)
			// in numeric order, so we can do an integer comparison of the masked attribute
			int accessLevel = 0;

			if (property.GetMethod != null) {
				int methodAccessLevel = (int)(property.GetMethod.Attributes & MethodAttributes.MemberAccessMask);
				if (accessLevel < methodAccessLevel) {
					accessLevel = methodAccessLevel;
					result = property.GetMethod.Attributes;
				}
			}

			if (property.SetMethod != null) {
				int methodAccessLevel = (int)(property.SetMethod.Attributes & MethodAttributes.MemberAccessMask);
				if (accessLevel < methodAccessLevel) {
					accessLevel = methodAccessLevel;
					result = property.SetMethod.Attributes;
				}
			}

			if (property.HasOtherMethods) {
				foreach (var m in property.OtherMethods) {
					int methodAccessLevel = (int)(m.Attributes & MethodAttributes.MemberAccessMask);
					if (accessLevel < methodAccessLevel) {
						accessLevel = methodAccessLevel;
						result = m.Attributes;
					}
				}
			}

			return result;
		}
	}

	[SuppressUnmanagedCodeSecurity]
	internal static class SafeNativeMethods
	{
		[DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
		public static extern int StrCmpLogicalW(string psz1, string psz2);
	}

	public sealed class NaturalStringComparer : IComparer<string>
	{
		public static readonly IComparer<string> Instance = new NaturalStringComparer();

		public int Compare(string a, string b)
		{
			return SafeNativeMethods.StrCmpLogicalW(a, b);
		}
	}
}
