﻿using Blazored.Modal;
using Blazored.Modal.Services;
using BlazorMonaco;
using Microsoft.AspNetCore.Components.Forms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeiEditor.Pages;

namespace TeiEditor
{
    public enum enmTagChanges
    {
        DoNothing = 0,
        OpenTag,
        CloseTag
    }

    public enum enmStatusColor
    {
        Done = 0,
        Current = 1
    }

    public static class Helpers
    {
        public static async Task<Position> getEndOfTag(string tagName, BlazorMonaco.Range tagRange, TextModel model)
        {
            //gets here only for non empty tags
            string endTag = $"</{tagName}>";
            int l = tagRange.EndLineNumber;
            List<string> lines = await model.GetLinesContent();
            int endTagCol = -1;
            while (l < lines.Count)
            {
                endTagCol = lines[l].IndexOf(endTag);
                if (endTagCol > -1) break;
                l++;
            }
            return new Position() { Column = endTagCol + endTag.Length + 1, LineNumber = l + 1 };
        }

        public static bool IsClosedTag(string tag)
        {
            return tag.IndexOf("/>") != -1;
        }

        public static async Task markTags(MonacoEditor editor, string tag,
            List<FindMatch> sourceMatches,
            Dictionary<string, BlazorMonaco.Range> sourceDecorations)
        {
            TextModel sourceModel = await editor.GetModel();
            await editor.ResetDeltaDecorations();
            List<ModelDeltaDecoration> lstDecorations = new List<ModelDeltaDecoration>();
            sourceMatches = await sourceModel.FindMatches($"<{tag}", false, false, false, null, true, 10000);
            if (sourceMatches.Count > 0)
            {
                foreach (FindMatch m in sourceMatches)
                {
                    m.Range = await Helpers.ExpandTagRange(m.Range, sourceModel);
                    lstDecorations.Add(new ModelDeltaDecoration
                    {
                        Range = m.Range,
                        Options = new ModelDecorationOptions
                        {
                            IsWholeLine = false,
                            ClassName = "decorationContent",
                            GlyphMarginClassName = "decorationGlyphMargin",
                            Minimap = new ModelDecorationMinimapOptions() { Position = MinimapPosition.Inline, Color = "#FFFF00" }//#90EE90 #FFFFFE
                        }
                    });
                }
                string[] decorations = await editor.DeltaDecorations(null, lstDecorations.ToArray());
                for (int i = 0; i < sourceMatches.Count; i++)
                {
                    sourceDecorations.Add(decorations[i], sourceMatches[i].Range);
                }
                await editor.RevealLineInCenter(sourceMatches[0].Range.StartLineNumber);
            }
        }

        public static async Task RemoveDecoration(MonacoEditor editor, string id)
        {
            string[] targetID = new string[] { id };
            List<ModelDeltaDecoration> emptyDec = new List<ModelDeltaDecoration>() { };
            await editor.DeltaDecorations(targetID, emptyDec.ToArray());
        }

        public static async Task<string> ColorRange(MonacoEditor editor, BlazorMonaco.Range range, string id = "", enmStatusColor statusColor = enmStatusColor.Done)
        {
            string contentClass = "";
            string GlyphMarginClass = "";
            string MiniMapColor = "";

            switch (statusColor)
            {
                case enmStatusColor.Done:
                    contentClass = "decorationContentDone";
                    GlyphMarginClass = "decorationGlyphMarginDone";
                    MiniMapColor = "#90EE90";
                    break;
                case enmStatusColor.Current:
                    contentClass = "decorationContentCurrent";
                    GlyphMarginClass = "decorationGlyphMarginCurrent";
                    MiniMapColor = "#bbfaf9";
                    break;
            }

            string[] targetID = new string[] { };
            if (!string.IsNullOrEmpty(id))
            {
                targetID = new string[] { id };
            }

            List<ModelDeltaDecoration> newDec = new List<ModelDeltaDecoration>(){
                new ModelDeltaDecoration
                {
                    Range = range,
                    Options = new ModelDecorationOptions
                    {
                        IsWholeLine = false,
                        ClassName = contentClass,
                        GlyphMarginClassName = GlyphMarginClass,
                        Minimap = new ModelDecorationMinimapOptions() { Position = MinimapPosition.Inline, Color = MiniMapColor }
                    }
                } };

            string[] decorations = await editor.DeltaDecorations(targetID, newDec.ToArray());
            return decorations[0];
        }

        public static JsonElement TagToJson(string Text, enmTagChanges tagChange)
        {
            switch (tagChange)
            {
                case enmTagChanges.OpenTag:
                    Text = Text.Replace("/>", ">");
                    break;
                case enmTagChanges.CloseTag:
                    if (Text.IndexOf("/") == -1) Text = Text.Replace(">", "/>");
                    break;
                case enmTagChanges.DoNothing:
                    break;
            }
            Text = Text.Replace("\"", "\\\"");
            var t = $"{{\"text\": \"{Text}\"}}";
            JsonDocument doc;
            doc = JsonDocument.Parse(t);
            return doc.RootElement;
        }
        public static async Task<BlazorMonaco.Range> ExpandTagRange(BlazorMonaco.Range matchRange, TextModel model)
        {
            BlazorMonaco.Range newRange = new BlazorMonaco.Range()
            {
                StartColumn = matchRange.StartColumn,
                StartLineNumber = matchRange.StartLineNumber
            };

            int tagCloseLine = matchRange.EndLineNumber;
            string line = await model.GetLineContent(tagCloseLine);
            int tagCloseCol = line.IndexOf(">", matchRange.EndColumn - 1);
            if (tagCloseCol == -1) //not found
            {
                tagCloseLine++;
                line = await model.GetLineContent(tagCloseLine);
                tagCloseCol = line.IndexOf(">");
            }
            tagCloseCol = tagCloseCol + 2;

            newRange.EndColumn = tagCloseCol;
            newRange.EndLineNumber = tagCloseLine;

            return newRange;
        }

        public static async Task<List<string>> LoadXMLfromFile(MonacoEditor editor, IBrowserFile file)
        {
            byte[] bytesOfXML = new byte[file.Size];
            using (Stream strm = file.OpenReadStream())
            {
                await strm.ReadAsync(bytesOfXML);
            }
            string stringOfXML = Encoding.UTF8.GetString(bytesOfXML);
            TextModel model = await MonacoEditorBase.CreateModel(stringOfXML, "xml");
            await editor.SetModel(model);

            List<string> lLines = stringOfXML.Split("\n").ToList<string>();
            return lLines;
        }

        public static void ShowModal(string msg, IModalService Modal)
        {
            ModalOptions options = new ModalOptions() { HideCloseButton = true };
            Modal.Show<Confirm>(msg, options);
        }

        public static async Task loadSchema(string path, string nameSpace, XmlSchemaSet schemaSet, HttpClient _client)
        {
            byte[] byteArrayS = await _client.GetByteArrayAsync(path);
            //Console.WriteLine($"{path}: {byteArrayS.Length}");
            MemoryStream streamS = new MemoryStream(byteArrayS);
            XmlReader xmlSchemaReader = XmlReader.Create(streamS);
            schemaSet.Add(nameSpace, xmlSchemaReader);
        }
    }
}
