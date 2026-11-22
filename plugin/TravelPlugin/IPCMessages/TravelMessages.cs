using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace TravelPlugin.IPCMessages
{
    public enum TravelOpcode
    {
        Press = 3101,
        Result = 3102
    }

    /// <summary>A travel hotkey was pressed on one character: every other injected character fires the same row.</summary>
    [AoContract((int)TravelOpcode.Press)]
    public class TravelPressMessage : IPCMessage
    {
        public override short Opcode => (short)TravelOpcode.Press;

        [AoMember(0)]
        public int PressId { get; set; }

        [AoMember(1)]
        public int Row { get; set; }

        /// <summary>What to do: see <see cref="TravelAction"/>. Added in 2.0.</summary>
        [AoMember(2)]
        public int Action { get; set; }
    }

    public enum TravelAction
    {
        Use = 0,        // cast the row's nano / use the row's item
        Cancel = 1,     // stop the row's travel nano
        CancelAll = 2,  // stop every travel nano set on any row
        Quit = 3        // 2.4: Ctrl+End - /quit on every character
    }

    /// <summary>What one character did after a press, so the open Travel window can show it.</summary>
    [AoContract((int)TravelOpcode.Result)]
    public class TravelResultMessage : IPCMessage
    {
        public override short Opcode => (short)TravelOpcode.Result;

        [AoMember(0)]
        public int PressId { get; set; }

        [AoMember(1)]
        public int Row { get; set; }

        [AoMember(2)]
        public bool Ok { get; set; }

        [AoMember(3, SerializeSize = ArraySizeType.Byte)]
        public string Character { get; set; }

        [AoMember(4, SerializeSize = ArraySizeType.Byte)]
        public string Text { get; set; }
    }
}
