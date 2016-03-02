using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Languages.Core.Text;
using Microsoft.R.Components.Extensions;
using Microsoft.R.Components.InteractiveWorkflow;
using Microsoft.R.Core.AST.Statements.Definitions;
using Microsoft.R.Core.Tokens;
using Microsoft.VisualStudio.InteractiveWindow;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;

namespace Microsoft.VisualStudio.R.Package.Repl {
    internal class RInteractiveWorkflowOperations : IRInteractiveWorkflowOperations {
        private ConcurrentQueue<PendingSubmission> _pendingInputs = new ConcurrentQueue<PendingSubmission>();
        private IInteractiveWindow _interactiveWindow;
        
        internal IInteractiveWindow InteractiveWindow {
            get { return _interactiveWindow; }
            set {
                if (Equals(_interactiveWindow, value)) {
                    return;
                }

                if (_interactiveWindow != null) {
                    _interactiveWindow.ReadyForInput -= ProcessQueuedInput;
                }
                _interactiveWindow = value;
                if (_interactiveWindow != null) {
                    _interactiveWindow.ReadyForInput += ProcessQueuedInput;
                }
            }
        }

        public void ExecuteExpression(string expression) {
            if (InteractiveWindow == null || InteractiveWindow.IsInitializing || string.IsNullOrWhiteSpace(expression)) {
                return;
            }

            InteractiveWindow.AddInput(expression);

            var buffer = InteractiveWindow.CurrentLanguageBuffer;
            var endPoint = InteractiveWindow.TextView.MapUpToBuffer(buffer.CurrentSnapshot.Length, buffer);
            if (endPoint != null) {
                InteractiveWindow.TextView.Caret.MoveTo(endPoint.Value);
            }

            InteractiveWindow.Operations.ExecuteInput();
        }

        public void ExecuteCurrentExpression(ITextView textView, Action<ITextView, ITextBuffer, int> formatDocument) {
            if (InteractiveWindow == null || InteractiveWindow.IsRunning) {
                return;
            }

            var curBuffer = InteractiveWindow.CurrentLanguageBuffer;
            var documentPoint = textView.MapDownToBuffer(textView.Caret.Position.BufferPosition, curBuffer);
            var text = curBuffer.CurrentSnapshot.GetText();
            if (!documentPoint.HasValue ||
                documentPoint.Value == documentPoint.Value.Snapshot.Length ||
                documentPoint.Value.Snapshot.Length == 0 ||
                !IsMultiLineCandidate(text)) {
                // Let the repl try and execute the code if the user presses enter at the
                // end of the buffer.
                if (InteractiveWindow.Evaluator.CanExecuteCode(text)) {
                    // If we know we can execute the code move the caret to the end of the
                    // current input, otherwise the interactive window won't execute it.  We
                    // have slightly more permissive handling here.
                    var point = textView.BufferGraph.MapUpToBuffer(
                        new SnapshotPoint(
                            curBuffer.CurrentSnapshot,
                            curBuffer.CurrentSnapshot.Length
                            ),
                        PointTrackingMode.Positive,
                        PositionAffinity.Successor,
                        textView.TextBuffer
                        );
                    textView.Caret.MoveTo(point.Value);
                }

                InteractiveWindow.Operations.Return();
            } else {
                // Otherwise insert a line break in the middle of an input
                InteractiveWindow.Operations.BreakLine();
                formatDocument(InteractiveWindow.TextView, curBuffer, Math.Max(documentPoint.Value - 1, 0));
            }
        }

        public void EnqueueExpression(string expression, bool addNewLine) {
            if (InteractiveWindow == null || InteractiveWindow.IsInitializing || InteractiveWindow.IsResetting) {
                return;
            }

            // add the input to our queue...
            _pendingInputs.Enqueue(new PendingSubmission { Expression = expression, AddNewLine = addNewLine });

            if (!InteractiveWindow.IsRunning) {
                // and process the queue if we weren't currently running
                ProcessQueuedInput();
            }
        }

        public void ReplaceCurrentExpression(string replaceWith) {
            if (InteractiveWindow == null || InteractiveWindow.IsInitializing) {
                return;
            }

            var textBuffer = InteractiveWindow.CurrentLanguageBuffer;
            var span = new Span(0, textBuffer.CurrentSnapshot.Length);
            if (!textBuffer.IsReadOnly(span)) {
                textBuffer.Replace(span, replaceWith);
            }
        }

        public void PositionCaretAtPrompt() {
            if (InteractiveWindow == null || InteractiveWindow.IsInitializing) {
                return;
            }

            var textView = InteractiveWindow.TextView;
            // Click on text view will move the caret so we need 
            // to move caret to the prompt after view finishes its
            // mouse processing.
            textView.Selection.Clear();
            ITextSnapshot snapshot = textView.TextBuffer.CurrentSnapshot;
            SnapshotPoint caretPosition = new SnapshotPoint(snapshot, snapshot.Length);
            textView.Caret.MoveTo(caretPosition);
        }

        public void ClearPendingInputs() {
            Interlocked.Exchange(ref _pendingInputs, new ConcurrentQueue<PendingSubmission>());
        }

        public Task<ExecutionResult> ResetAsync() {
            return InteractiveWindow.Operations.ResetAsync();
        }

        public void Dispose() {
            if (_interactiveWindow != null) {
                _interactiveWindow.ReadyForInput -= ProcessQueuedInput;
            }
        }

        private void ProcessQueuedInput() {
            if (InteractiveWindow == null) {
                return;
            }

            var textView = InteractiveWindow.TextView;

            // Process all of our pending inputs until we get a complete statement
            PendingSubmission current;
            while (_pendingInputs.TryDequeue(out current)) {
                var curLangBuffer = InteractiveWindow.CurrentLanguageBuffer;
                SnapshotPoint? curLangPoint = null;

                // If anything is selected we need to clear it before inserting new code
                textView.Selection.Clear();

                // Find out if caret position is where code can be inserted.
                // Caret must be in the area mappable to the language buffer.
                if (!textView.Caret.InVirtualSpace) {
                    curLangPoint = textView.MapDownToBuffer(textView.Caret.Position.BufferPosition, curLangBuffer);
                }

                if (curLangPoint == null) {
                    // Ensure the caret is in the input buffer, otherwise inserting code does nothing.
                    SnapshotPoint? viewPoint = textView.BufferGraph.MapUpToBuffer(
                        new SnapshotPoint(curLangBuffer.CurrentSnapshot, curLangBuffer.CurrentSnapshot.Length),
                        PointTrackingMode.Positive,
                        PositionAffinity.Predecessor,
                        textView.TextBuffer);

                    if (!viewPoint.HasValue) {
                        // Unable to map language buffer to view.
                        // Try moving caret to the end of the view then.
                        viewPoint = new SnapshotPoint(textView.TextBuffer.CurrentSnapshot, textView.TextBuffer.CurrentSnapshot.Length);
                    }

                    if (viewPoint.HasValue) {
                        textView.Caret.MoveTo(viewPoint.Value);
                    }
                }

                InteractiveWindow.InsertCode(current.Expression);
                string fullCode = curLangBuffer.CurrentSnapshot.GetText();

                if (InteractiveWindow.Evaluator.CanExecuteCode(fullCode)) {
                    // the code is complete, execute it now
                    InteractiveWindow.Operations.ExecuteInput();
                    return;
                }

                if (current.AddNewLine) {
                    // We want a new line after non-complete inputs, e.g. the user ctrl-entered on
                    // function() {
                    InteractiveWindow.InsertCode(textView.Options.GetNewLineCharacter());
                }
            }
        }

        private static bool IsMultiLineCandidate(string text) {
            if (text.IndexOfAny(new[] { '\n', '\r' }) != -1) {
                // if we already have newlines then we're multiline
                return true;
            }

            var tokenizer = new RTokenizer();
            IReadOnlyTextRangeCollection<RToken> tokens = tokenizer.Tokenize(new TextStream(text), 0, text.Length);
            return tokens.Any(t => t.TokenType == RTokenType.OpenCurlyBrace);
        }

        private class PendingSubmission {
            public string Expression { get; set; }
            public bool AddNewLine { get; set; }
        }
    }
}