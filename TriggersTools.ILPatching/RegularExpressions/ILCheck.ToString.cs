﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TriggersTools.ILPatching.Internal;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TriggersTools.ILPatching.RegularExpressions {
	partial class ILCheck {
		#region Prefixes

		/// <summary>
		/// The prefixes used for angle-braced opchecks.
		/// </summary>
		private static readonly Dictionary<OpChecks, string> opCheckPrefixes = new Dictionary<OpChecks, string> {
			{ OpChecks.Nop, "nop" },

			{ OpChecks.OpCode, "op" },
			{ OpChecks.OpCodeOperand, "op" },

			{ OpChecks.Operand, "cap" },
			{ OpChecks.OperandEquals, "ceq" },

			{ OpChecks.FieldName, "fld" },
			{ OpChecks.MethodName, "mth" },
			{ OpChecks.TypeName, "typ" },
			{ OpChecks.CallSiteName, "cls" },
		};

		/// <summary>
		/// Gets the string prefix used for the specified opcheck.
		/// </summary>
		/// <param name="opCheck">The opcheck to get the prefix for.</param>
		/// <returns>The prefix for the opcheck.</returns>
		/// 
		/// <exception cref="ArgumentException">
		/// <paramref name="opCheck"/> is not an angle-braced opcheck.
		/// </exception>
		public static string GetPrefix(OpChecks opCheck) {
			if (!opCheckPrefixes.TryGetValue(opCheck, out string prefix))
				throw new ArgumentException($"ILCheck code {opCheck} does not have a prefix!");
			return prefix;
		}
		/// <summary>
		/// Gets the <see cref="OpChecks"/> value for the prefix type.
		/// </summary>
		/// <param name="prefix">The prefix to parse.</param>
		/// <returns>The opcheck code for the prefix.</returns>
		/// 
		/// <remarks>
		/// Both <see cref="OpChecks.OpCode"/> and <see cref="OpChecks.OpCodeOperand"/> use the same prefix
		/// "op", <see cref="OpChecks.OpCode"/> will be returned when "op" is passed.
		/// <para/>
		/// Prefixes are case-insensitive.
		/// </remarks>
		/// 
		/// <exception cref="FormatException">
		/// <paramref name="prefix"/> is not a valid prefix code.
		/// </exception>
		public static OpChecks ParsePrefix(string prefix) {
			var pair = opCheckPrefixes.FirstOrDefault(p => p.Value.Equals(prefix, StringComparison.InvariantCultureIgnoreCase));
			if (pair.Key == OpChecks.Nop) // Not found
				throw new FormatException($"Unknown ILCheck prefix code \"{prefix}\"!");
			return pair.Key;
		}

		#endregion

		#region ToString

		/// <summary>
		/// Gets the string representation of the ILCheck with no format.
		/// </summary>
		/// <returns>The string representation of the ILCheck.</returns>
		public override string ToString() {
			return ToString(string.Empty, CultureInfo.CurrentCulture);
		}
		/// <summary>
		/// Gets the string representation of the ILCheck with the specified format.
		/// </summary>
		/// <param name="format">The string format to use.</param>
		/// <returns>The string representation of the ILCheck.</returns>
		/// 
		/// <remarks>
		/// ILCheck Format Flags:
		/// Nq = No quantifier: The quantifier is not added, even when not exactly one.
		/// U  = Upper: The prefix codes are output in uppercase.
		/// I  = Indented: Prefix codes are indented to all align.
		/// <para/>
		/// Quantifier Format Flags:
		/// Bo = Brace format only: Shorthand tokens ?/*/+ will not be used.
		/// M  = Mandatory: String will not be empty if <see cref="ILQuantifier.IsOne"/> is true.
		/// </remarks>
		/// 
		/// <exception cref="FormatException">
		/// Unknown flags were passed in <paramref name="format"/>.
		/// </exception>
		public string ToString(string format) {
			return ToString(format, CultureInfo.CurrentCulture);
		}
		/// <summary>
		/// Gets the string representation of the ILCheck with the specified format.
		/// </summary>
		/// <param name="format">The string format to use.</param>
		/// <param name="formatProvider">Unused.</param>
		/// <returns>The string representation of the ILCheck.</returns>
		/// 
		/// <remarks>
		/// ILCheck Format Flags:
		/// Nq = No quantifier: The quantifier is not added, even when not exactly one.
		/// U  = Upper: The prefix codes are output in uppercase.
		/// I  = Indented: Prefix codes are indented to all align.
		/// <para/>
		/// Quantifier Format Flags:
		/// Bo = Brace format only: Shorthand tokens ?/*/+ will not be used.
		/// M  = Mandatory: String will not be empty if <see cref="ILQuantifier.IsOne"/> is true.
		/// </remarks>
		/// 
		/// <exception cref="FormatException">
		/// Unknown flags were passed in <paramref name="format"/>.
		/// </exception>
		public string ToString(string format, IFormatProvider formatProvider) {
			bool noQuantifier = FormatUtils.HasToken("Nq", ref format);

			string originalFormat = format;
			string s = ToStringNoQuantifier(ref format);
			if (!noQuantifier || format.Length != 0) {
				try {
					// Test format validity
					// An error will be thrown if the remaining quantifier format is not valid.
					string quantifierStr = Quantifier.ToString(format);
					if (!noQuantifier)
						s += quantifierStr;
				} catch (FormatException) {
					throw new FormatException($"Invalid ILCheck format \"{originalFormat}\"!");
				}
			}
			return s;
		}
		private string ToStringNoQuantifier(ref string format) {
			bool upper = FormatUtils.HasToken("U", ref format);
			bool indented = FormatUtils.HasToken("I", ref format);

			opCheckPrefixes.TryGetValue(Code, out string prefix);
			if (prefix != null) {
				if (upper)
					prefix = prefix.ToUpper();
				if (indented && Code != OpChecks.Nop)
					prefix = prefix.PadRight(3);
			}

			string opCode = OpCode.ToString();
			/*if (indentedOpCode && Code != OpChecks.OpCode && (Code != OpChecks.Operand || CaptureName != null)) {
				opCode = opCode.PadRight(AnyOpCode.LongestOpCodeName);
			}*/

			switch (Code) {
			case OpChecks.Nop: return $"<{prefix}>";
			case OpChecks.Alternative: return "|";
			case OpChecks.Start: return "^";
			case OpChecks.End: return "$";
			case OpChecks.Skip: return ".";

			case OpChecks.GroupEnd: return ")";
			case OpChecks.GroupStart:
				if (!IsCapture)
					return "(?:";
				else if (CaptureName != null)
					return $"(?'{CaptureName}'";
				return "(";

			case OpChecks.OpCode:
				return $"<{prefix} {opCode}>";
			case OpChecks.OpCodeOperand:
				return $"<{prefix} {opCode}  {OperandToString(Operand)}>";

			case OpChecks.Operand:
				if (CaptureName != null)
					return $"<{prefix} {opCode}  '{CaptureName}'>";
				else
					return $"<{prefix} {opCode}>";
			case OpChecks.OperandEquals:
				if (CaptureName != null)
					return $"<{prefix} {opCode}  '{CaptureName}'>";
				else
					return $"<{prefix} {opCode}  '{CaptureIndex}'>";

			case OpChecks.FieldName:
			case OpChecks.MethodName:
			case OpChecks.TypeName:
			case OpChecks.CallSiteName:
				return $"<{prefix} {opCode}  \"{MemberName}\">";

			case OpChecks.Quantifier:
				return string.Empty; // Quantifiers are not shown in this method
			}
			return $"UNK: {Code}";
		}
		private string DebuggerDisplay => ToString();
		public void Print() {
			Print("I1", CultureInfo.CurrentCulture);
		}
		public void Print(int indent) {
			Print($"I{indent}", CultureInfo.CurrentCulture);
		}
		public void Print(string format) {
			Print(format, CultureInfo.CurrentCulture);
		}
		public void Print(string format, IFormatProvider formatProvider) {
			bool noQuantifier = FormatUtils.HasToken("Nq", ref format);

			string originalFormat = format;
			PrintNoQuantifier(ref format);
			if (!noQuantifier || format.Length != 0) {
				try {
					// Test format validity
					// An error will be thrown if the remaining quantifier format is not valid.
					string quantifierStr = Quantifier.ToString(format);
					if (!noQuantifier)
						Console.Write(quantifierStr);
				} catch (FormatException) {
					throw new FormatException($"Invalid ILCheck format \"{originalFormat}\"!");
				}
			}
			Console.ResetColor();
			Console.WriteLine();
		}
		private void PrintNoQuantifier(ref string format) {
			bool upper = FormatUtils.HasToken("U", ref format);
			bool indented = FormatUtils.HasToken("I", ref format);

			opCheckPrefixes.TryGetValue(Code, out string prefix);
			if (prefix != null) {
				if (upper)
					prefix = prefix.ToUpper();
				if (indented && Code != OpChecks.Nop)
					prefix = prefix.PadRight(3);
			}

			string opCode = OpCode.ToString();
			/*if (indentedOpCode && Code != OpChecks.OpCode && (Code != OpChecks.Operand || CaptureName != null)) {
				opCode = opCode.PadRight(AnyOpCode.LongestOpCodeName);
			}*/

			if (prefix != null) {
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.Write($"<{prefix}");
				if (OpCode.IsMulti)
					Console.ForegroundColor = ConsoleColor.Cyan;
				else
					Console.ForegroundColor = ConsoleColor.Blue;
				Console.Write($" {opCode}");
			}

			string operand = null;

			switch (Code) {
			case OpChecks.Nop: break;
			case OpChecks.Alternative:
				Console.ForegroundColor = ConsoleColor.Gray;
				Console.Write("|");
				break;
			case OpChecks.Start:
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.Write("^");
				break;
			case OpChecks.End:
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.Write("$");
				break;
			case OpChecks.Skip:
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.Write(".");
				break;

			case OpChecks.GroupEnd:
				Console.ForegroundColor = ConsoleColor.DarkGreen;
				Console.Write(")");
				break;
			case OpChecks.GroupStart:
				Console.ForegroundColor = ConsoleColor.DarkGreen;
				if (!IsCapture)
					Console.Write("(?:");
				else if (CaptureName != null) {
					Console.Write("(?");
					Console.ForegroundColor = ConsoleColor.Green;
					Console.Write($"'{CaptureName}'");
				}
				else
					Console.Write("(");
				break;

			case OpChecks.OpCode: break;
			case OpChecks.OpCodeOperand:
				operand = OperandToString(Operand); break;

			case OpChecks.Operand:
				if (CaptureName != null)
					operand = $"'{CaptureName}'"; break;
			case OpChecks.OperandEquals:
				if (CaptureName != null)
					operand = $"'{CaptureName}'";
				else
					operand = $"'{CaptureIndex}'";
				break;

			case OpChecks.FieldName:
			case OpChecks.MethodName:
			case OpChecks.TypeName:
			case OpChecks.CallSiteName:
				operand = $"\"{MemberName}\"";
				break;

			case OpChecks.Quantifier:
				break; // Quantifiers are not shown in this method
			}
			if (operand != null) {
				if (Regex.IsMatch(operand, @"^\w+:")) {
					Console.ForegroundColor = ConsoleColor.Magenta;
				}
				else if (Regex.IsMatch(operand, @"^'(?:[^'\\]|\\.)*'")) {
					Console.ForegroundColor = ConsoleColor.Green;
				}
				else if (Regex.IsMatch(operand, @"^""(?:[^""\\]|\\.)*""")) {
					Console.ForegroundColor = ConsoleColor.Red;
				}
				else {
					Console.ForegroundColor = ConsoleColor.Gray;
				}
				Console.Write($"  {operand}");
			}
			if (prefix != null) {
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.Write(">");
			}
			//Console.ResetColor();
		}


		public static string OperandToString(object operand) {
			if (operand == null)
				return "null";
			switch (operand) {
			//case bool value:
			//	return value.ToString();

			case int value:
				return $"{value}";
			case long value:
				return $"{value}L";
			case byte value:
				return $"{value}b";
			case sbyte value:
				return $"{value}sb";
			case float value:
				return $"{value}f";
			case double value:
				return $"{value}d";
			case string value:
				return $"\"{value}\"";

			case ParameterDefinition value:
				return $"arg:\"{value}\"";
			case VariableDefinition value:
				return $"loc:\"{value}\"";
			case FieldReference value:
				return $"fld:\"{value.FullName.Substring(value.FullName.IndexOf(' ') + 1)}\"";
			case MethodReference value:
				return $"mth:\"{value.FullName.Substring(value.FullName.IndexOf(' ') + 1)}\"";
			case TypeReference value:
				return $"typ:\"{value.FullName}\"";
			case CallSite value:
				return $"cal:\"{value.FullName}\"";

			case Instruction value:
				return $"ins:{value}";
			case Instruction[] value:
				return $"ina:[{string.Join(",", (object[]) value)}]";
			}
			return null;
		}

		#endregion
	}
}