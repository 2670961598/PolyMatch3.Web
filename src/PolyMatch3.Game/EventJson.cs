using System.Text;
using PolyMatch3.Samples.Bomb;
using PolyMatch3.Samples.Classic;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Game
{
    /// <summary>
    /// 游戏事件 → JSON 手写序列化（零反射，WASM 裁剪友好）。
    /// 结构：{seq, step, type, cells, ...}；Fall 带 fromTo，Spawn 带 pieces，Match 带 priority/variant。
    /// </summary>
    public static class EventJson
    {
        public static string Serialize(in GameEventEnvelope env)
        {
            var ev = env.Event;
            var sb = new StringBuilder(128);
            sb.Append("{\"seq\":").Append(env.Seq)
              .Append(",\"step\":\"").Append(env.StepName)
              .Append("\",\"type\":\"").Append(ev.Type).Append('"');
            AppendInts(sb, "cells", ev.CellIds);

            switch (ev)
            {
                case MatchEvent m:
                    sb.Append(",\"priority\":").Append(m.Priority)
                      .Append(",\"variant\":").Append(m.VariantIndex);
                    break;
                case FallEvent f:
                    AppendInts(sb, "fromTo", f.FromTo);
                    break;
                case SpawnEvent s:
                    AppendInts(sb, "pieces", s.PieceTypes);
                    break;
                case BombSpawnEvent b:
                    sb.Append(",\"kind\":").Append(b.Kind);
                    break;
                case SpecialSpawnEvent ss:
                    sb.Append(",\"kind\":").Append(ss.Kind);
                    break;
                case TransformEvent t:
                    sb.Append(",\"kind\":").Append(t.Kind);
                    break;
            }

            return sb.Append('}').ToString();
        }

        private static void AppendInts(StringBuilder sb, string name, int[] values)
        {
            sb.Append(",\"").Append(name).Append("\":[");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i]);
            }
            sb.Append(']');
        }
    }
}
