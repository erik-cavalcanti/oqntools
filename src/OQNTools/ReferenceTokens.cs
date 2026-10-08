using System;
using System.Collections.Generic;
namespace OQNTools
{
    public static class ReferenceTokens
    {
        public static string Match(string stable,IDictionary<string,string> known)
        {
            foreach(var token in (stable??"").Split(':'))if(known.TryGetValue(token,out var kind))return kind;
            return null;
        }
    }
}
