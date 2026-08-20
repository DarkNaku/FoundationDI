using System.Collections.Generic;
using Random = System.Random;

namespace DarkNaku.FoundationDI
{
    internal static class ShuffleExtensions
    {
        private static readonly Random Rng = new();

        /// <summary>큐의 순서를 제자리에서 무작위로 섞는다.</summary>
        public static void Shuffle<T>(this Queue<T> queue)
        {
            var list = new List<T>(queue);

            queue.Clear();

            int n = list.Count;

            while (n > 1)
            {
                n--;
                int k = Rng.Next(n + 1);
                (list[n], list[k]) = (list[k], list[n]);
            }

            foreach (var item in list)
            {
                queue.Enqueue(item);
            }
        }
    }
}
