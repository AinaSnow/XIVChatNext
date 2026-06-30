using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml.Documents;

namespace XIVChat_Desktop {
    public static class Markdown {
        private readonly static char[] Delimiters = {
            '*', '_', '~',
        };

        public static IEnumerable<Inline> MarkdownToInlines(string input) {
            var nodes = ParseNodes(input);
            return NodesToInlines(nodes);
        }

        private static ContainerNode ParseNodes(string input) {
            var root = new ContainerNode();
            DelimiterNode? run = null;
            char? last = null;
            var escaping = false;

            var segment = new StringBuilder();

            void AddTextRun() {
                if (segment.Length == 0) {
                    return;
                }

                root.AddChild(new TextNode(root, segment.ToString()));
                segment.Clear();
            }

            void ProcessExistingRun() {
                // determine the flank
                run.DetermineFlank();

                // if flank is still unknown, this is an invalid run
                var isUnknown = run.flank == DelimiterNode.Flank.Unknown;
                // also if strikethrough does not contain two delimiters, this is an invalid run
                var notLongEnough = run.character == '~' && run.length < 2;
                if (isUnknown || notLongEnough) {
                    // return these characters to the string
                    for (var i = 0; i < run.length; i++) {
                        segment.Append(run.character);
                    }

                    // remove the run
                    goto RemoveRun;
                }

                // at this point, we're going to add the run, so also add the text if necessary
                AddTextRun();

                // add the run to the list
                root.AddChild(run);

                RemoveRun:
                // remove the run from working area
                run = null;
            }

            var chars = input.ToCharArray();
            foreach (var c in chars) {
                var append = true;

                if (c == '\\' && !escaping) {
                    escaping = true;
                    append = false;
                }

                // these characters can form delimiter runs
                if (Delimiters.Contains(c) && !escaping) {
                    // don't add this character to the text segment
                    append = false;

                    if (run != null && run.character != c) {
                        run.following = c;
                        ProcessExistingRun();
                    }

                    // create a run if necessary
                    run ??= new DelimiterNode(root, c) {
                        preceding = last,
                        length = 0,
                        originalLength = 0,
                    };

                    // increase the run's length
                    run.length += 1;
                    run.originalLength += 1;

                    // skip to the end
                    goto Finish;
                }

                // the last character was a delimiter but this character is not
                // we know that this character is not a delimiter because of the goto
                if (run != null && last != null && Delimiters.Contains((char)last)) {
                    // set the following character to this character
                    run.following = c;

                    ProcessExistingRun();
                }

                Finish:
                last = c;
                if (!append) {
                    continue;
                }

                if (escaping) {
                    escaping = false;

                    if (!c.IsAsciiPunctuation()) {
                        segment.Append('\\');
                    }
                }

                segment.Append(c);
            }

            // re-add backslashes as final character
            if (escaping) {
                segment.Append('\\');
            }

            // if we ended on a run, process it
            if (run != null) {
                ProcessExistingRun();
            }

            AddTextRun();

            return root;
        }

        private static IEnumerable<Inline> NodesToInlines(MarkdownNodeWithChildren root) {
            var openersBottom = new Dictionary<int, Dictionary<char, LinkedListNode<DelimiterNode>?>>();
            for (var i = 0; i < 3; i++) {
                openersBottom[i] = new Dictionary<char, LinkedListNode<DelimiterNode>?>();
                foreach (var c in Delimiters) {
                    openersBottom[i][c] = null;
                }
            }

            var delims = new LinkedList<DelimiterNode>();
            var node = root.Children.First;
            while (node != null) {
                if (node.Value is DelimiterNode dRun) {
                    delims.AddLast(dRun);
                }

                node = node.Next;
            }

            // no emphasis to process, so just return all nodes as-is
            if (delims.Count == 0) {
                return root.Children.Select(child => child.ToInline());
            }

            // move forward looking for closers
            var closer = delims.First;
            while (closer != null) {
                if (!closer.Value.CanClose) {
                    closer = closer.Next;
                    continue;
                }

                // found first emphasis closer. now look back for first matching opener
                var opener = closer.Previous;
                var openerFound = false;
                var bottom = openersBottom[closer.Value.originalLength % 3][closer.Value.character];
                while (opener != null && opener != bottom) {
                    var oddMatch =
                        (closer.Value.CanOpen || opener.Value.CanClose) &&
                        closer.Value.originalLength % 3 != 0 &&
                        (opener.Value.originalLength + closer.Value.originalLength) % 3 == 0;
                    if (opener.Value.character == closer.Value.character && opener.Value.CanOpen && !oddMatch) {
                        openerFound = true;
                        break;
                    }

                    opener = opener.Previous;
                }

                var oldCloser = closer;

                // emphasis and strikethrough
                // https://spec.commonmark.org/0.29/#emphasis-and-strong-emphasis
                // https://github.github.com/gfm/#strikethrough-extension-
                if (Delimiters.Contains(closer.Value.character)) {
                    if (!openerFound) {
                        closer = closer.Next;
                    } else {
                        if (opener == null) {
                            throw new InvalidOperationException();
                        }

                        MarkdownNodeWithChildren emph;

                        var isStrike = closer.Value.character == '~';
                        if (isStrike) {
                            opener.Value.length -= 2;
                            closer.Value.length -= 2;

                            emph = new StrikethroughNode(root);
                        } else {
                            var useDelims = closer.Value.length >= 2 && opener.Value.length >= 2 ? 2 : 1;

                            opener.Value.length -= useDelims;
                            closer.Value.length -= useDelims;

                            emph = useDelims == 1 ? (MarkdownNodeWithChildren)new EmphasisNode(root) : new StrongNode(root);
                        }

                        var runsOpener = root.Children.Find(opener.Value);
                        var runsCloser = root.Children.Find(closer.Value);
                        var tmp = runsOpener!.Next;
                        while (tmp != null && tmp != runsCloser) {
                            emph.AddChild(tmp.Value);

                            var oldTmp = tmp;
                            tmp = tmp.Next;
                            root.Children.Remove(oldTmp);
                            if (oldTmp.Value is DelimiterNode dNode) {
                                delims.Remove(dNode);
                            }
                        }

                        root.Children.AddAfter(runsOpener, emph);

                        if (opener.Value.length == 0) {
                            delims.Remove(opener);
                            root.Children.Remove(runsOpener);
                        }

                        if (closer.Value.length == 0) {
                            closer = closer.Next;
                            delims.Remove(oldCloser);
                            root.Children.Remove(runsCloser!);
                        }
                    }
                }

                if (openerFound) {
                    continue;
                }

                openersBottom[oldCloser.Value.originalLength % 3][oldCloser.Value.character] = oldCloser.Previous;

                if (!oldCloser.Value.CanOpen) {
                    delims.Remove(oldCloser);
                }
            }

            return root.Children.Select(child => child.ToInline());
        }
    }

    public abstract class MarkdownNode {
        public MarkdownNode? Parent { get; set; }

        protected MarkdownNode(MarkdownNode? parent = null) {
            this.Parent = parent;
        }

        public abstract Inline ToInline();
    }

    public class TextNode : MarkdownNode {
        public string Text { get; set; }

        public TextNode(MarkdownNode parent, string text) : base(parent) {
            this.Text = text;
        }

        public override Inline ToInline() => new Run { Text = this.Text };
    }

    public class DelimiterNode : MarkdownNode {
        public enum Flank {
            Unknown,
            Left,
            Right,
            Both,
        }

        public readonly char character;
        public char? preceding;
        public char? following;
        public int originalLength;
        public int length;
        public Flank flank = Flank.Unknown;

        public bool CanOpen => this.CalcCanOpen();
        public bool CanClose => this.CalcCanClose();

        public DelimiterNode(MarkdownNode parent, char character) : base(parent) {
            this.character = character;
        }

        public override Inline ToInline() {
            var text = new StringBuilder(this.length);

            for (var i = 0; i < this.length; i++) {
                text.Append(this.character);
            }

            return new Run { Text = text.ToString() };
        }

        private bool CalcCanOpen() {
            switch (this.character) {
                case '*' when (this.flank == Flank.Left || this.flank == Flank.Both):
                case '_' when (this.flank == Flank.Left || (this.flank == Flank.Both && this.preceding?.IsAsciiPunctuation() == true)):
                case '~' when (this.length >= 2 && (this.flank == Flank.Left || this.flank == Flank.Both)):
                    return true;
                default:
                    return false;
            }
        }

        private bool CalcCanClose() {
            switch (this.character) {
                case '*' when (this.flank == Flank.Right || this.flank == Flank.Both):
                case '_' when (this.flank == Flank.Right || (this.flank == Flank.Both && this.following?.IsAsciiPunctuation() == true)):
                case '~' when (this.length >= 2 && (this.flank == Flank.Right || this.flank == Flank.Both)):
                    return true;
                default:
                    return false;
            }
        }

        public void DetermineFlank() {
            var followedByWhitespace = this.following?.IsWhitespace() ?? true;
            var followedByPunctuation = this.following?.IsAsciiPunctuation() ?? false;
            var precededByWhitespace = this.preceding?.IsWhitespace() ?? true;
            var precededByPunctuation = this.preceding?.IsAsciiPunctuation() ?? false;

            var isLeft = false;
            var isRight = false;

            // A left-flanking delimiter run is a delimiter run that is (1) not followed by Unicode whitespace, and
            // either (2a) not followed by a punctuation character, or (2b) followed by a punctuation character and
            // preceded by Unicode whitespace or a punctuation character. For purposes of this definition, the beginning
            // and the end of the line count as Unicode whitespace.
            if (!followedByWhitespace && (!followedByPunctuation || (followedByPunctuation && (precededByWhitespace || precededByPunctuation)))) {
                isLeft = true;
            }

            // A right-flanking delimiter run is a delimiter run that is (1) not preceded by Unicode whitespace, and
            // either (2a) not preceded by a punctuation character, or (2b) preceded by a punctuation character and
            // followed by Unicode whitespace or a punctuation character. For purposes of this definition, the beginning
            // and the end of the line count as Unicode whitespace.
            if (!precededByWhitespace && (!precededByPunctuation || (precededByPunctuation && (followedByWhitespace || followedByPunctuation)))) {
                isRight = true;
            }

            if (isLeft && isRight) {
                this.flank = Flank.Both;
            } else if (isLeft) {
                this.flank = Flank.Left;
            } else if (isRight) {
                this.flank = Flank.Right;
            }
        }
    }

    public class EmphasisNode : MarkdownNodeWithChildren {
        public EmphasisNode(MarkdownNode parent) : base(parent) { }

        public override Inline ToInline() {
            var span = new Span {
                FontStyle = global::Windows.UI.Text.FontStyle.Italic,
            };
            foreach (var child in this.Children) {
                span.Inlines.Add(child.ToInline());
            }
            return span;
        }
    }

    public class StrongNode : MarkdownNodeWithChildren {
        public StrongNode(MarkdownNode parent) : base(parent) { }

        public override Inline ToInline() {
            var span = new Span {
                FontWeight = global::Microsoft.UI.Text.FontWeights.Bold,
            };
            foreach (var child in this.Children) {
                span.Inlines.Add(child.ToInline());
            }
            return span;
        }
    }

    public class StrikethroughNode : MarkdownNodeWithChildren {
        public StrikethroughNode(MarkdownNode parent) : base(parent) { }

        public override Inline ToInline() {
            var span = new Span();
            foreach (var child in this.Children) {
                span.Inlines.Add(child.ToInline());
            }
            return span;
        }
    }

    public abstract class MarkdownNodeWithChildren : MarkdownNode {
        public LinkedList<MarkdownNode> Children { get; } = new LinkedList<MarkdownNode>();

        protected MarkdownNodeWithChildren(MarkdownNode? parent) : base(parent) { }

        public void AddChild(MarkdownNode node) {
            node.Parent = this;
            this.Children.AddLast(node);
        }

        /// <summary>
        /// Replace this node with its children in its parent's children.
        /// </summary>
        public void PopUp() {
            if (!(this.Parent is MarkdownNodeWithChildren parent)) {
                return;
            }

            var us = parent.Children.Find(this);

            if (us == null) {
                return;
            }

            foreach (var child in this.Children) {
                child.Parent = parent;
                parent.Children.AddBefore(us, child);
                parent.Children.Remove(us);
            }
        }
    }

    public class ContainerNode : MarkdownNodeWithChildren {
        public ContainerNode(MarkdownNode? parent = null) : base(parent) { }

        public override Inline ToInline() {
            var span = new Span();
            foreach (var child in this.Children) {
                span.Inlines.Add(child.ToInline());
            }
            return span;
        }
    }
}
