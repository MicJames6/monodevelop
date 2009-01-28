// MarkupSyntaxMode.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace Mono.TextEditor.Highlighting
{
	public class MarkupSyntaxMode : SyntaxMode
	{
		class Tag
		{
			public string Command {
				get;
				set;
			}
			public Dictionary<string, string> Arguments {
				get;
				private set;
			}
			
			public Tag ()
			{
				Arguments = new Dictionary<string, string> ();
			}
			
			public static Tag Parse (string text)
			{
				Tag result = new Tag ();
				string[] commands = text.Split (' ', '\t');
				result.Command = commands[0];
				for (int i = 1; i < commands.Length; i++) {
					string[] argument = commands[i].Split ('=');
					if (argument.Length == 2)
						result.Arguments[argument[0]] = argument[1].Trim ('"');
				}
				return result;
			}
		}

		class TextChunk : Chunk
		{
			string text;
			
			public TextChunk (ChunkStyle style, int offset, string text)
			{
				this.text = text;
				this.Offset = offset;
				this.Length = text.Length;
				this.Style = style;
			}
			
			public override char GetCharAt (Document doc, int offset)
			{
				return text [offset - this.Offset];
			}
		}
		
		static ChunkStyle GetChunkStyle (Style style, IEnumerable<Tag> tagStack)
		{
			ChunkStyle result = new ChunkStyle ();
			result.Color = style.Default;
			foreach (Tag tag in tagStack) {
				//System.Console.WriteLine("'" + tag.Command + "'");
				switch (tag.Command.ToUpper ()) {
				case "B":
					result.Bold = true;
					break;
				case "SPAN":
					if (tag.Arguments.ContainsKey ("style")) {
						ChunkStyle chunkStyle =  style.GetChunkStyle (tag.Arguments["style"]);
						if (chunkStyle != null) {
							result.Color = chunkStyle.Color;
							result.Bold = chunkStyle.Bold;
							result.Italic = chunkStyle.Italic;
						} else {
							throw new Exception ("Style " + tag.Arguments["style"] + " not found.");
						}
					}
					if (tag.Arguments.ContainsKey ("foreground")) 
						result.Color = style.GetColorFromString (tag.Arguments["foreground"]);
					if (tag.Arguments.ContainsKey ("background")) 
						result.BackgroundColor = style.GetColorFromString (tag.Arguments["background"]);
					break;
				case "A":
					result.Link = tag.Arguments["ref"];
					break;
				case "I":
					result.Italic = true;
					break;
				case "U":
					result.Underline = true;
					break;
				}
			}
			return result;
		}
		
		public override string GetTextWithoutMarkup (Document doc, Style style, int offset, int length)
		{
			StringBuilder result = new StringBuilder ();
			
			int curOffset = offset;
			int endOffset =  offset + length;
			
			while (curOffset < endOffset) {
				LineSegment curLine = doc.GetLineByOffset (curOffset);
				for (Chunk chunk = GetChunks (doc, style, curLine, curOffset, System.Math.Min (endOffset - curOffset, curLine.EndOffset - curOffset)); chunk != null; chunk = chunk.Next) {
					for (int i = 0; i < chunk.Length; i++) {
						result.Append (chunk.GetCharAt (doc, chunk.Offset + i));
					}
				}
				curOffset += curLine.Length;
				if (curOffset < endOffset)
					result.AppendLine ();
			}
			return result.ToString ();
		}
		
		public override Chunk GetChunks (Document doc, Style style, LineSegment line, int offset, int length)
		{
			int endOffset = System.Math.Min (offset + length, doc.Length);
			Stack<Tag> tagStack = new Stack<Tag> ();
			Chunk curChunk = new Chunk (offset, 0, new ChunkStyle ());
			Chunk startChunk = curChunk;
			Chunk endChunk = curChunk;
			bool inTag = true, inSpecial = false;
			int tagBegin = -1, specialBegin = -1;
			for (int i = offset; i < endOffset; i++) {
				char ch = doc.GetCharAt (i);
				switch (ch) {
				case '<':
					curChunk.Length = i - curChunk.Offset;
					if (curChunk.Length > 0) {
						curChunk.Style = GetChunkStyle (style, tagStack);
						endChunk = endChunk.Next = curChunk;
						curChunk = new Chunk (i, 0, null);
					}
					tagBegin = i;
					inTag = true;
					break;
				case '&':
					inSpecial = true;
					specialBegin = i;
					break;
				case ';':
					if (inSpecial) {
						string specialText = doc.GetTextBetween (specialBegin + 1, i);
						curChunk.Length = specialBegin - curChunk.Offset;
						if (curChunk.Length > 0) {
							curChunk.Style = GetChunkStyle (style, tagStack);
							endChunk = endChunk.Next = curChunk;
							curChunk = new Chunk (i, 0, null);
						}
						switch (specialText) {
						case "lt":
							endChunk = endChunk.Next = new TextChunk (GetChunkStyle (style, tagStack), specialBegin, "<");
							break;
						case "gt": 
							endChunk = endChunk.Next = new TextChunk (GetChunkStyle (style, tagStack), specialBegin, ">");
							break;
						case "amp": 
							endChunk = endChunk.Next = new TextChunk (GetChunkStyle (style, tagStack), specialBegin, "&");
							break;
						}
						curChunk.Offset = i + 1;
						inSpecial = false;
					}
					break;
				case '>':
					if (!inTag)
						break;
					string tagText = doc.GetTextBetween (tagBegin + 1, i);
					if (tagText.StartsWith ("/")) {
						if (tagStack.Count > 0)
							tagStack.Pop ();
					} else {
						tagStack.Push (Tag.Parse (tagText));
					}
					curChunk.Offset = i + 1;
					inTag = false;
					break;
				}
			}
			curChunk.Length = endOffset - curChunk.Offset;
			if (curChunk.Length > 0) {
				curChunk.Style = GetChunkStyle (style, tagStack);
				endChunk = endChunk.Next = curChunk;
			}
			endChunk.Next = null;
			return startChunk;
		}
	}
}
