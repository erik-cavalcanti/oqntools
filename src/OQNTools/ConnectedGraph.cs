using System;
using System.Collections.Generic;

namespace OQNTools
{
    // Iterative traversal: physical connectivity, not membership in an MEP system.
    internal static class ConnectedGraph
    {
        public static bool ReachesThrough<T>(T start,T target,Func<T,IEnumerable<T>> neighbors,Func<T,bool> allowedIntermediate)
        {
            var comparer=EqualityComparer<T>.Default;var visited=new HashSet<T>{start};var queue=new Queue<T>();queue.Enqueue(start);
            while(queue.Count>0)
                foreach(var next in neighbors(queue.Dequeue()))
                {
                    if(comparer.Equals(next,target))return true;
                    if(visited.Add(next)&&allowedIntermediate(next))queue.Enqueue(next);
                }
            return false;
        }
        public static HashSet<T> Collect<T>(T start, Func<T,IEnumerable<T>> neighbors)
        {
            var found=new HashSet<T>{start};var queue=new Queue<T>();queue.Enqueue(start);
            while(queue.Count>0)
                foreach(var next in neighbors(queue.Dequeue()))
                    if(found.Add(next))queue.Enqueue(next);
            return found;
        }
    }
}
