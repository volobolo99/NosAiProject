using System;
using System.Collections.Generic;
using System.Threading;
using WindowsInput.Native;

namespace WindowsInput
{
    /// <summary>
    /// Implements IKeyboardSimulator by dispatching INPUT messages.
    /// Upstream: michaelnoonan/inputsimulator
    /// Commit: a61df64303cb76b005a3ced19eae4cb4755e2e42
    /// License: MIT (verify upstream LICENSE before product reuse).
    /// </summary>
    public class KeyboardSimulator : IKeyboardSimulator
    {
        private readonly IInputSimulator _inputSimulator;
        private readonly IInputMessageDispatcher _messageDispatcher;

        public KeyboardSimulator(IInputSimulator inputSimulator)
        {
            if (inputSimulator == null) throw new ArgumentNullException("inputSimulator");
            _inputSimulator = inputSimulator;
            _messageDispatcher = new WindowsInputMessageDispatcher();
        }

        internal KeyboardSimulator(IInputSimulator inputSimulator, IInputMessageDispatcher messageDispatcher)
        {
            if (inputSimulator == null) throw new ArgumentNullException("inputSimulator");
            if (messageDispatcher == null) throw new InvalidOperationException("A valid input dispatcher is required.");
            _inputSimulator = inputSimulator;
            _messageDispatcher = messageDispatcher;
        }

        public IMouseSimulator Mouse { get { return _inputSimulator.Mouse; } }

        private void ModifiersDown(InputBuilder builder, IEnumerable<VirtualKeyCode> modifierKeyCodes)
        {
            if (modifierKeyCodes == null) return;
            foreach (var key in modifierKeyCodes) builder.AddKeyDown(key);
        }

        private void ModifiersUp(InputBuilder builder, IEnumerable<VirtualKeyCode> modifierKeyCodes)
        {
            if (modifierKeyCodes == null) return;
            var stack = new Stack<VirtualKeyCode>(modifierKeyCodes);
            while (stack.Count > 0) builder.AddKeyUp(stack.Pop());
        }

        private void KeysPress(InputBuilder builder, IEnumerable<VirtualKeyCode> keyCodes)
        {
            if (keyCodes == null) return;
            foreach (var key in keyCodes) builder.AddKeyPress(key);
        }

        private void SendSimulatedInput(INPUT[] inputList) => _messageDispatcher.DispatchInput(inputList);

        public IKeyboardSimulator KeyDown(VirtualKeyCode keyCode)
        {
            SendSimulatedInput(new InputBuilder().AddKeyDown(keyCode).ToArray());
            return this;
        }

        public IKeyboardSimulator KeyUp(VirtualKeyCode keyCode)
        {
            SendSimulatedInput(new InputBuilder().AddKeyUp(keyCode).ToArray());
            return this;
        }

        public IKeyboardSimulator KeyPress(VirtualKeyCode keyCode)
        {
            SendSimulatedInput(new InputBuilder().AddKeyPress(keyCode).ToArray());
            return this;
        }

        public IKeyboardSimulator KeyPress(params VirtualKeyCode[] keyCodes)
        {
            var builder = new InputBuilder();
            KeysPress(builder, keyCodes);
            SendSimulatedInput(builder.ToArray());
            return this;
        }

        public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<VirtualKeyCode> modifierKeyCodes, IEnumerable<VirtualKeyCode> keyCodes)
        {
            var builder = new InputBuilder();
            ModifiersDown(builder, modifierKeyCodes);
            KeysPress(builder, keyCodes);
            ModifiersUp(builder, modifierKeyCodes);
            SendSimulatedInput(builder.ToArray());
            return this;
        }

        public IKeyboardSimulator TextEntry(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var inputList = new InputBuilder().AddCharacters(text).ToArray();
            SendSimulatedInput(inputList);
            return this;
        }

        public IKeyboardSimulator Sleep(int millisecondsTimeout)
        {
            Thread.Sleep(millisecondsTimeout);
            return this;
        }

        public IKeyboardSimulator Sleep(TimeSpan timeout)
        {
            Thread.Sleep(timeout);
            return this;
        }
    }
}
