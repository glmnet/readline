using System;
using System.Collections.Generic;
using System.Text;
using Internal.ReadLine.Abstractions;

namespace Internal.ReadLine
{
    internal class KeyHandler
    {
        private const char WhiteSpace = ' ';
        private int _cursorPosition = 0;
        private StringBuilder _text;
        private List<string> _history;
        private int _historyIndex;
        private Dictionary<ConsoleModifiers, Dictionary<ConsoleKey, Action>> _keyActionsModifiers;
        private Dictionary<ConsoleKey, Action> _keyActions;
        private string[] _completions;
        private string _completionPrefix;
        private int _completionsIndex;
        private IConsole Console2;

        public char[] WordSeparators = new[] { ' ', '/' };

        private bool IsInAutoCompleteMode() => _completions != null;

        private int CursorPosition
        {
            get => _cursorPosition;
            set
            {
                if (value < 0 || value > _text.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                var bufferWidth = Console2.BufferWidth;
                var offset = value - _cursorPosition;
                var col = Math.Abs(((offset % bufferWidth) + Console2.CursorLeft + bufferWidth) % bufferWidth);
                var promptLength = Math.Abs(Console2.CursorLeft - (_cursorPosition % bufferWidth));
                var originRow = (promptLength + _cursorPosition) / bufferWidth;
                var destRow = (promptLength + value) / bufferWidth;
                var row = Console2.CursorTop + destRow - originRow;
                Console2.SetCursorPosition(col, row);
                _cursorPosition = value;
            }
        }

        private void Write(string value)
        {
            Console2.Write(value);
            _cursorPosition += value.Length;
        }

        private void MoveCursorLeft()
        {
            if (CursorPosition > 0)
            {
                CursorPosition--;
            }
        }

        private void MoveCursorHome() => CursorPosition = 0;

        private void MoveCursorRight()
        {
            if (CursorPosition < _text.Length)
            {
                CursorPosition++;
            }
        }

        private void MoveCursorEnd() => CursorPosition = _text.Length;

        private void MoveCursorLeftWord()
        {
            if (CursorPosition == 0)
            {
                return;
            }

            var buffer = _text.ToString(0, CursorPosition);
            var trimmedBuffer = buffer.TrimEnd();
            var trimEnd = buffer.Length - trimmedBuffer.Length;
            var pos = trimmedBuffer.LastIndexOfAny(WordSeparators, CursorPosition - trimEnd - 1) + 1;
            if (pos < 0)
            {
                pos = 0;
            }

            CursorPosition = pos;
        }

        private void MoveCursorRightWord()
        {
            if (CursorPosition == _text.Length)
            {
                return;
            }

            var buffer = _text.ToString(CursorPosition, _text.Length - CursorPosition);
            var trimmedBuffer = buffer.TrimStart();
            var trimStart = buffer.Length - trimmedBuffer.Length;
            var pos = buffer.IndexOfAny(WordSeparators, trimStart);
            if (pos < 0)
            {
                CursorPosition = _text.Length;
            }
            else
            {
                CursorPosition += pos;
            }
        }

        private void ClearBuffer()
        {
            CursorPosition = 0;
            Write(new string(WhiteSpace, _text.Length));
            CursorPosition = 0;
            _text.Clear();
        }

        private void ClearToStart()
        {
            var pos = CursorPosition;
            var length = _text.Length;
            _text.Remove(0, CursorPosition);
            var clear = _text.ToString() + new string(WhiteSpace, CursorPosition);
            MoveCursorHome();
            Write(clear);
            MoveCursorHome();
        }

        private void ClearToEnd()
        {
            var pos = CursorPosition;
            var clear = new string(WhiteSpace, _text.Length - pos);
            Write(clear);
            CursorPosition = pos;
            _text.Remove(pos, _text.Length - pos);
        }

        private void SetBufferString(string str)
        {
            var bufferLength = _text.Length;
            _text.Clear().Append(str);
            MoveCursorHome();
            if (str.Length < bufferLength)
            {
                Write(_text.ToString() + new string(WhiteSpace, bufferLength - str.Length));
            }
            else
            {
                Write(_text.ToString());
            }

            MoveCursorEnd();
        }

        private void WriteChar(char c)
        {
            if (char.IsControl(c))
            {
                return;
            }

            int insertPos = CursorPosition;
            if (insertPos >= _text.Length)
            {
                _text.Append(c);
                Write(c.ToString());
            }
            else
            {
                _text.Insert(insertPos, c);
                Write(_text.ToString(insertPos, _text.Length - insertPos));
                CursorPosition = insertPos + 1;
            }
        }

        private void Backspace()
        {
            int removePos = CursorPosition - 1;
            if (removePos > -1)
            {
                _text.Remove(removePos, 1);
                CursorPosition = removePos;
                Write(_text.ToString(removePos, _text.Length - removePos) + " ");
                CursorPosition = removePos;
            }
            else
            {
                ResetAutoComplete();
            }
        }

        private void Delete()
        {
            int deletePos = CursorPosition;
            if (deletePos == _text.Length)
            {
                return;
            }

            _text.Remove(deletePos, 1);
            Write(_text.ToString(deletePos, _text.Length - deletePos) + " ");
            CursorPosition = deletePos;
        }

        private void TransposeChars()
        {
            if (CursorPosition == 0)
            {
                return;
            }

            if (CursorPosition == _text.Length)
            {
                CursorPosition -= 2;
            }
            else
            {
                CursorPosition -= 1;
            }

            var transpose = _text.ToString(CursorPosition, 2);
            _text[CursorPosition] = transpose[1];
            _text[CursorPosition + 1] = transpose[0];
            Write(transpose[1].ToString() + transpose[0]);
        }

        private void StartAutoComplete()
        {
            _completionsIndex = 0;
            WriteAutoComplete();
        }

        private void NextAutoComplete()
        {
            _completionsIndex++;

            if (_completionsIndex == _completions.Length)
            {
                _completionsIndex = 0;
            }

            WriteAutoComplete();
        }

        private void PreviousAutoComplete()
        {
            _completionsIndex--;

            if (_completionsIndex == -1)
            {
                _completionsIndex = _completions.Length - 1;
            }

            WriteAutoComplete();
        }

        private void WriteAutoComplete()
        {
            SetBufferString(_completionPrefix + _completions[_completionsIndex]);
        }

        private void PrevHistory()
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
                SetBufferString(_history[_historyIndex]);
            }
        }

        private void NextHistory()
        {
            if (_historyIndex < _history.Count)
            {
                _historyIndex++;
                if (_historyIndex != _history.Count)
                {
                    SetBufferString(_history[_historyIndex]);
                }
            }
        }

        private void ResetAutoComplete()
        {
            _completions = null;
            _completionsIndex = 0;
        }

        public string Text
        {
            get
            {
                return _text.ToString();
            }
        }

        public void CutPreviousWord()
        {
            if (CursorPosition < 1)
            {
                return;
            }

            var buffer = _text.ToString();
            var trimEndChars = buffer.Length - buffer.TrimEnd().Length + 1;
            var previousSpace = buffer.LastIndexOf(WhiteSpace, CursorPosition - trimEndChars);
            int removeChars;
            if (previousSpace == -1)
            {
                removeChars = CursorPosition;
            }
            else
            {
                removeChars = CursorPosition - previousSpace - 1;
            }

            _text.Remove(CursorPosition - removeChars, removeChars);
            var newCursorPosition = CursorPosition - removeChars;

            CursorPosition = newCursorPosition;
            Write(_text.ToString(newCursorPosition, _text.Length - newCursorPosition) + new string(WhiteSpace, removeChars));
            CursorPosition = newCursorPosition;
        }

        public KeyHandler(IConsole console, List<string> history, IAutoCompleteHandler autoCompleteHandler)
        {
            Console2 = console;

            _history = history ?? new List<string>();
            _historyIndex = _history.Count;
            _text = new StringBuilder();
            _keyActionsModifiers = new Dictionary<ConsoleModifiers, Dictionary<ConsoleKey, Action>>
            {
                { ConsoleModifiers.Control, new Dictionary<ConsoleKey, Action> {
                    { ConsoleKey.A, MoveCursorHome },
                    { ConsoleKey.B, MoveCursorLeft },
                    { ConsoleKey.D, Delete },
                    { ConsoleKey.E, MoveCursorEnd },
                    { ConsoleKey.F, MoveCursorRight },
                    { ConsoleKey.H, Backspace },
                    { ConsoleKey.K, ClearToEnd },
                    { ConsoleKey.L, ClearBuffer },
                    { ConsoleKey.N, NextHistory },
                    { ConsoleKey.P, PrevHistory },
                    { ConsoleKey.T, TransposeChars },
                    { ConsoleKey.U, ClearToStart },
                    { ConsoleKey.W, CutPreviousWord },
                    { ConsoleKey.LeftArrow, MoveCursorLeftWord },
                    { ConsoleKey.RightArrow, MoveCursorRightWord }
                } },
                { ConsoleModifiers.Shift, new Dictionary<ConsoleKey, Action> {
                    { ConsoleKey.Tab, () => {
                        if (IsInAutoCompleteMode())
                        {
                            PreviousAutoComplete();
                        } } }
                } }
            };

            _keyActions = new Dictionary<ConsoleKey, Action>
            {
                { ConsoleKey.LeftArrow, MoveCursorLeft },
                { ConsoleKey.Home, MoveCursorHome },
                { ConsoleKey.End, MoveCursorEnd },
                { ConsoleKey.RightArrow, MoveCursorRight },
                { ConsoleKey.Backspace, Backspace },
                { ConsoleKey.Delete, Delete },
                { ConsoleKey.Escape, ClearBuffer },
                { ConsoleKey.UpArrow, PrevHistory },
                { ConsoleKey.DownArrow, NextHistory },
                { ConsoleKey.Tab, () => {
                    if (IsInAutoCompleteMode())
                    {
                        NextAutoComplete();
                    }
                    else
                    {
                        if (autoCompleteHandler == null)
{                            return;
}
                        string text = _text.ToString();

                        var completionStart = text.LastIndexOfAny(autoCompleteHandler.Separators);
                        completionStart = completionStart == -1 ? 0 : completionStart + 1;
                        _completionPrefix = text.Substring(0, completionStart);
                        _completions = autoCompleteHandler.GetSuggestions(text, completionStart);

                        _completions = _completions?.Length == 0 ? null : _completions;

                        if (_completions == null)
{                            return;
}
                        StartAutoComplete();
                    } } }
            };
        }

        public void Handle(ConsoleKeyInfo keyInfo)
        {
            // If in auto complete mode and Tab wasn't pressed
            if (IsInAutoCompleteMode() && keyInfo.Key != ConsoleKey.Tab)
            {
                ResetAutoComplete();
            }

            if (!_keyActionsModifiers.TryGetValue(keyInfo.Modifiers, out Dictionary<ConsoleKey, Action> d))
            {
                d = _keyActions;
            }

            if (d.TryGetValue(keyInfo.Key, out Action action))
            {
                action();
            }
            else
            {
                WriteChar(keyInfo.KeyChar);
            }
        }
    }
}
