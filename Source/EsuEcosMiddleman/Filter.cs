using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EsuEcosMiddleman.ECoS;

namespace EsuEcosMiddleman
{
    internal class Filter
    {
        public static bool IsFiltered(ICfgFilter filter, ICommand command, ILogger logger = null)
        {
            if (filter == null) return false;
            if (command == null) return false;

            var objId = command.ObjectId;
            if (objId == -1) return true;

            if (filter.ObjectIds.Contains(objId)) return true;

            foreach (var equ in filter.ObjectIdRanges)
            {
                var equT = equ?.Trim();
                if (string.IsNullOrEmpty(equT)) continue;
                if(equT.Length < 2) continue;
                var c0 = equT[0];
                var c1 = equT[1];

                try
                {
                    if (c0 == '>' && c1 == '=') // >= 
                        return objId >= GetMathPartner(equT, 2);

                    if (c0 == '<' && c1 == '=') // <=
                        return objId <= GetMathPartner(equT, 2);

                    if (c0 == '=' && c1 == '=') // ==
                        return objId == GetMathPartner(equT, 2);

                    if (c0 == '<') // <
                        return objId < GetMathPartner(equT, 1);

                    if (c0 == '>') // >
                        return objId > GetMathPartner(equT, 1);
                }
                catch(Exception ex)
                {
                    logger?.Log.Error($"{ex.GetExceptionMessages()}");

                    continue;
                }
            }

            return false;
        }

        private static int GetMathPartner(string equ, int startIndex)
        {
            var v = equ.Substring(startIndex)?.Trim();
            if (string.IsNullOrEmpty(v)) return -1;
            if (int.TryParse(v, out var vv))
                return vv;

            throw new ArgumentOutOfRangeException($"{v} is out of range");
        }
    }
}
