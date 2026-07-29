using System;

namespace PolyMatch3.Samples
{
    /// <summary>
    /// 棋子种类平行数组（炸弹系地基）：与棋盘颜色层等长，0=普通，1..N=特殊种类。
    /// 铁律：颜色层怎么动，kind 层就怎么同步动——交换/下落必须成对调用 Swap/Move，
    /// 消除必须成对调用 Clear。本示例的 KindSwapStep / KindGravityStep 是标准写法。
    /// </summary>
    public sealed class KindLayer
    {
        public const int Normal = 0;
        public const int Bomb3x3 = 1;

        private readonly int[] _kinds;

        public KindLayer(int cellCount)
        {
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            _kinds = new int[cellCount];
        }

        public int Get(int cellId) => _kinds[cellId];

        public void Set(int cellId, int kind) => _kinds[cellId] = kind;

        public void Clear(int cellId) => _kinds[cellId] = Normal;

        /// <summary>移动同步：from 的种类交给 to，from 归普通（跟随颜色层移动）。</summary>
        public void Move(int from, int to)
        {
            _kinds[to] = _kinds[from];
            _kinds[from] = Normal;
        }

        /// <summary>交换同步（跟随颜色层交换）。</summary>
        public void Swap(int a, int b)
        {
            (_kinds[a], _kinds[b]) = (_kinds[b], _kinds[a]);
        }
    }
}
