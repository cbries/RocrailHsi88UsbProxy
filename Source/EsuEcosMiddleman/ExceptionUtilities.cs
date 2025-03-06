// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EsuEcosMiddleman
{
    internal static class ExceptionUtilities
    {
        public static IEnumerable<Exception> GetInnerExceptions(this Exception ex)
        {
            Exception innerException = ex ?? throw new ArgumentNullException(nameof(ex));
            do
            {
                yield return innerException;
                innerException = innerException.InnerException;
            } while (innerException != null);
        }

        public static string GetInnerExceptionsAsString(this Exception ex)
        {
            return string.Join<Exception>(Environment.NewLine, ex.GetInnerExceptions());
        }

        public static string GetExceptionMessages(this Exception ex)
        {
            if (string.IsNullOrEmpty(ex?.Message)) return "Exception instance is null.";

            return ex.Message
                   + Environment.NewLine
                   + string.Join(Environment.NewLine, ex.GetInnerExceptions());
        }

        public static void ShowException(this Exception ex)
        {
            Trace.WriteLine($"{ex.GetExceptionMessages()}");
        }
    }
}
