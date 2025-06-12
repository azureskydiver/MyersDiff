/// diff.cs: A port of the algorithm to C#
/// Copyright (c) by Matthias Hertel, http://www.mathertel.de
/// This work is licensed under a BSD style license. See http://www.mathertel.de/License.aspx

namespace my.Utilities.Diff
{
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// This Class implements the Difference Algorithm published in
    /// "An O(ND) Difference Algorithm and its Variations" by Eugene Myers
    /// Algorithmica Vol. 1 No. 2, 1986, p 251.
    /// 
    /// See [documentation](docu.md) for more details and change log.
    /// </summary>

    public class Diff
    {
        public struct Item
        {
            public int StartLineA;
            public int StartLineB;

            public int DeletedACount;
            public int DeletedBCount;
        }

        /// <summary>
        /// Find the difference in 2 texts, comparing by textlines.
        /// </summary>
        /// <param name="textA">A-version of the text (usually the old one)</param>
        /// <param name="textB">B-version of the text (usually the new one)</param>
        /// <returns>Returns a array of Items that describe the differences.</returns>
        public IEnumerable<Item> DiffText(string textA, string textB)
            => DiffText(textA, textB, false, false, false);

        /// <summary>
        /// Find the difference in 2 text documents, comparing by textlines.
        /// The algorithm itself is comparing 2 arrays of numbers so when comparing 2 text documents
        /// each line is converted into a (hash) number. This hash-value is computed by storing all
        /// textlines into a common hashtable so i can find duplicates in there, and generating a 
        /// new number each time a new textline is inserted.
        /// </summary>
        /// <param name="textA">A-version of the text (usually the old one)</param>
        /// <param name="textB">B-version of the text (usually the new one)</param>
        /// <param name="trimSpace">When set to true, all leading and trailing whitespace characters are stripped out before the comparision is done.</param>
        /// <param name="ignoreSpace">When set to true, all whitespace characters are converted to a single space character before the comparision is done.</param>
        /// <param name="ignoreCase">When set to true, all characters are converted to their lowercase equivalence before the comparision is done.</param>
        /// <returns>Returns a array of Items that describe the differences.</returns>
        public static IEnumerable<Item> DiffText(string textA, string textB, bool trimSpace, bool ignoreSpace, bool ignoreCase)
        {
            var h = new Dictionary<string, int>(textA.Length + textB.Length);
            var dataA = new DiffData(DiffCodes(textA, h, trimSpace, ignoreSpace, ignoreCase));
            var dataB = new DiffData(DiffCodes(textB, h, trimSpace, ignoreSpace, ignoreCase));
            h.Clear();

            int max = dataA.Length + dataB.Length + 1;
            var downVector = new int[2 * max + 2];
            var upVector = new int[2 * max + 2];

            LCS(dataA, 0, dataA.Length, dataB, 0, dataB.Length, downVector, upVector);

            Optimize(dataA);
            Optimize(dataB);
            return CreateDiffs(dataA, dataB);
        }

        /// <summary>
        /// If a sequence of modified lines starts with a line that contains the same content
        /// as the line that appends the changes, the difference sequence is modified so that the
        /// appended line and not the starting line is marked as modified.
        /// This leads to more readable diff sequences when comparing text files.
        /// </summary>
        /// <param name="data">A Diff data buffer containing the identified changes.</param>
        static void Optimize(DiffData data)
        {
            int startPos, endPos;

            startPos = 0;
            while (startPos < data.Length)
            {
                while ((startPos < data.Length) && (data.Modified[startPos] == false))
                    startPos++;
                endPos = startPos;
                while ((endPos < data.Length) && (data.Modified[endPos] == true))
                    endPos++;

                if ((endPos < data.Length) && (data.Data[startPos] == data.Data[endPos]))
                {
                    data.Modified[startPos] = false;
                    data.Modified[endPos] = true;
                }
                else
                {
                    startPos = endPos;
                }
            }
        }

        /// <summary>
        /// Find the difference in 2 arrays of integers.
        /// </summary>
        /// <param name="arrayA">A-version of the numbers (usually the old one)</param>
        /// <param name="arrayB">B-version of the numbers (usually the new one)</param>
        /// <returns>Returns a array of Items that describe the differences.</returns>
        public static IEnumerable<Item> DiffInt(int[] arrayA, int[] arrayB)
        {
            var dataA = new DiffData(arrayA);
            var dataB = new DiffData(arrayB);

            int max = dataA.Length + dataB.Length + 1;
            var downVector = new int[2 * max + 2];
            var upVector = new int[2 * max + 2];

            LCS(dataA, 0, dataA.Length, dataB, 0, dataB.Length, downVector, upVector);
            return CreateDiffs(dataA, dataB);
        }

        /// <summary>
        /// This function converts all textlines of the text into unique numbers for every unique textline
        /// so further work can work only with simple numbers.
        /// </summary>
        /// <param name="aText">the input text</param>
        /// <param name="h">This extern initialized hashtable is used for storing all ever used textlines.</param>
        /// <param name="trimSpace">ignore leading and trailing space characters</param>
        /// <returns>a array of integers.</returns>
        static int[] DiffCodes(string aText, Dictionary<string, int> h, bool trimSpace, bool ignoreSpace, bool ignoreCase)
        {
            // strip off all cr, only use lf as textline separator.
            aText = aText.Replace("\r", "");
            var lines = aText.Split('\n');
            var codes = new int[lines.Length];

            int lastUsedCode = h.Count;
            for (int i = 0; i < lines.Length; ++i)
            {
                string s = lines[i];
                if (trimSpace)
                    s = s.Trim();

                if (ignoreSpace)
                    s = Regex.Replace(s, "\\s+", " ");    // TODO: optimization: faster blank removal.

                if (ignoreCase)
                    s = s.ToLower();

                if (!h.ContainsKey(s))
                {
                    lastUsedCode++;
                    h[s] = lastUsedCode;
                    codes[i] = lastUsedCode;
                }
                else
                {
                    codes[i] = h[s];
                }
            }
            return codes;
        }


        /// <summary>
        /// This is the algorithm to find the Shortest Middle Snake (SMS).
        /// </summary>
        /// <param name="dataA">sequence A</param>
        /// <param name="lowerA">lower bound of the actual range in DataA</param>
        /// <param name="upperA">upper bound of the actual range in DataA (exclusive)</param>
        /// <param name="dataB">sequence B</param>
        /// <param name="lowerB">lower bound of the actual range in DataB</param>
        /// <param name="upperB">upper bound of the actual range in DataB (exclusive)</param>
        /// <param name="downVector">a vector for the (0,0) to (x,y) search. Passed as a parameter for speed reasons.</param>
        /// <param name="upVector">a vector for the (u,v) to (N,M) search. Passed as a parameter for speed reasons.</param>
        /// <returns>a MiddleSnakeData record containing x,y and u,v</returns>
        static (int X, int Y) SMS(DiffData dataA, int lowerA, int upperA,
                                  DiffData dataB, int lowerB, int upperB,
                                  int[] downVector, int[] upVector)
        {
            int max = dataA.Length + dataB.Length + 1;

            int downK = lowerA - lowerB; // the k-line to start the forward search
            int upK = upperA - upperB; // the k-line to start the reverse search

            int delta = (upperA - lowerA) - (upperB - lowerB);
            bool oddDelta = (delta & 1) != 0;

            // The vectors in the publication accepts negative indexes. the vectors implemented here are 0-based
            // and are access using a specific offset: UpOffset UpVector and DownOffset for DownVector
            int downOffset = max - downK;
            int upOffset = max - upK;

            int maxD = ((upperA - lowerA + upperB - lowerB) / 2) + 1;

            // Debug.Write(2, "SMS", String.Format("Search the box: A[{0}-{1}] to B[{2}-{3}]", LowerA, UpperA, LowerB, UpperB));

            downVector[downOffset + downK + 1] = lowerA;
            upVector[upOffset + upK - 1] = upperA;

            for (int d = 0; d <= maxD; d++)
            {
                // Extend the forward path.
                for (int k = downK - d; k <= downK + d; k += 2)
                {
                    // Debug.Write(0, "SMS", "extend forward path " + k.ToString());

                    // find the only or better starting point
                    int x;
                    if (k == downK - d)
                    {
                        x = downVector[downOffset + k + 1]; // down
                    }
                    else
                    {
                        x = downVector[downOffset + k - 1] + 1; // a step to the right
                        if ((k < downK + d) && (downVector[downOffset + k + 1] >= x))
                            x = downVector[downOffset + k + 1]; // down
                    }
                    int y = x - k;

                    // find the end of the furthest reaching forward D-path in diagonal k.
                    while ((x < upperA) && (y < upperB) && (dataA.Data[x] == dataB.Data[y]))
                    {
                        x++;
                        y++;
                    }
                    downVector[downOffset + k] = x;

                    // overlap ?
                    if (oddDelta && (upK - d < k) && (k < upK + d))
                    {
                        if (upVector[upOffset + k] <= downVector[downOffset + k])
                        {
                            int X = downVector[downOffset + k];
                            int Y = downVector[downOffset + k] - k;
                            // ret.u = UpVector[UpOffset + k];      // 2002.09.20: no need for 2 points 
                            // ret.v = UpVector[UpOffset + k] - k;
                            return (X, Y);
                        }
                    }
                }

                // Extend the reverse path.
                for (int k = upK - d; k <= upK + d; k += 2)
                {
                    // Debug.Write(0, "SMS", "extend reverse path " + k.ToString());

                    // find the only or better starting point
                    int x;
                    if (k == upK + d)
                    {
                        x = upVector[upOffset + k - 1]; // up
                    }
                    else
                    {
                        x = upVector[upOffset + k + 1] - 1; // left
                        if ((k > upK - d) && (upVector[upOffset + k - 1] < x))
                            x = upVector[upOffset + k - 1]; // up
                    }
                    int y = x - k;

                    while ((x > lowerA) && (y > lowerB) && (dataA.Data[x - 1] == dataB.Data[y - 1]))
                    {
                        x--;
                        y--;
                    }
                    upVector[upOffset + k] = x;

                    // overlap ?
                    if (!oddDelta && (downK - d <= k) && (k <= downK + d))
                    {
                        if (upVector[upOffset + k] <= downVector[downOffset + k])
                        {
                            int X = downVector[downOffset + k];
                            int Y = downVector[downOffset + k] - k;
                            // ret.u = UpVector[UpOffset + k];     // 2002.09.20: no need for 2 points 
                            // ret.v = UpVector[UpOffset + k] - k;
                            return (X, Y);
                        }
                    }
                }
            }

            throw new System.ApplicationException("the algorithm should never come here.");
        }


        /// <summary>
        /// This is the divide-and-conquer implementation of the longes common-subsequence (LCS) 
        /// algorithm.
        /// The published algorithm passes recursively parts of the A and B sequences.
        /// To avoid copying these arrays the lower and upper bounds are passed while the sequences stay constant.
        /// </summary>
        /// <param name="dataA">sequence A</param>
        /// <param name="lowerA">lower bound of the actual range in DataA</param>
        /// <param name="upperA">upper bound of the actual range in DataA (exclusive)</param>
        /// <param name="dataB">sequence B</param>
        /// <param name="lowerB">lower bound of the actual range in DataB</param>
        /// <param name="upperB">upper bound of the actual range in DataB (exclusive)</param>
        /// <param name="downVector">a vector for the (0,0) to (x,y) search. Passed as a parameter for speed reasons.</param>
        /// <param name="upVector">a vector for the (u,v) to (N,M) search. Passed as a parameter for speed reasons.</param>
        static void LCS(DiffData dataA, int lowerA, int upperA,
                        DiffData dataB, int lowerB, int upperB,
                        int[] downVector, int[] upVector)
        {
            // Debug.Write(2, "LCS", String.Format("Analyse the box: A[{0}-{1}] to B[{2}-{3}]", LowerA, UpperA, LowerB, UpperB));

            // Fast walkthrough equal lines at the start
            while (lowerA < upperA && lowerB < upperB && dataA.Data[lowerA] == dataB.Data[lowerB])
            {
                lowerA++;
                lowerB++;
            }

            // Fast walkthrough equal lines at the end
            while (lowerA < upperA && lowerB < upperB && dataA.Data[upperA - 1] == dataB.Data[upperB - 1])
            {
                --upperA;
                --upperB;
            }

            if (lowerA == upperA)
            {
                // mark as inserted lines.
                while (lowerB < upperB)
                    dataB.Modified[lowerB++] = true;
            }
            else if (lowerB == upperB)
            {
                // mark as deleted lines.
                while (lowerA < upperA)
                    dataA.Modified[lowerA++] = true;
            }
            else
            {
                // Find the middle snake and length of an optimal path for A and B
                (int x, int y) = SMS(dataA, lowerA, upperA, dataB, lowerB, upperB, downVector, upVector);
                // Debug.Write(2, "MiddleSnakeData", String.Format("{0},{1}", smsrd.x, smsrd.y));

                // The path is from LowerX to (x,y) and (x,y) to UpperX
                LCS(dataA, lowerA, x, dataB, lowerB, y, downVector, upVector);
                LCS(dataA, x, upperA, dataB, y, upperB, downVector, upVector);  // 2002.09.20: no need for 2 points 
            }
        }


        /// <summary>Scan the tables of which lines are inserted and deleted,
        /// producing an edit script in forward order.  
        /// </summary>
        // dynamic array
        static IEnumerable<Item> CreateDiffs(DiffData dataA, DiffData dataB)
        {
            int lineA = 0;
            int lineB = 0;
            while (lineA < dataA.Length || lineB < dataB.Length)
            {
                if ((lineA < dataA.Length) && (!dataA.Modified[lineA])
                    && (lineB < dataB.Length) && (!dataB.Modified[lineB]))
                {
                    // equal lines
                    lineA++;
                    lineB++;
                }
                else
                {
                    // maybe deleted and/or inserted lines
                    int startA = lineA;
                    int startB = lineB;

                    // while (LineA < DataA.Length && DataA.modified[LineA])
                    while (lineA < dataA.Length && (lineB >= dataB.Length || dataA.Modified[lineA]))
                        lineA++;

                    // while (LineB < DataB.Length && DataB.modified[LineB])
                    while (lineB < dataB.Length && (lineA >= dataA.Length || dataB.Modified[lineB]))
                        lineB++;

                    if ((startA < lineA) || (startB < lineB))
                    {
                        yield return new Item()
                        {
                            StartLineA = startA,
                            StartLineB = startB,
                            DeletedACount = lineA - startA,
                            DeletedBCount = lineB - startB,
                        };
                    }
                }
            }
        }
    }

    /// <summary>Data on one input file being compared.  
    /// </summary>
    class DiffData
    {
        /// <summary>Number of elements (lines).</summary>
        public int Length => Data.Length;

        /// <summary>Buffer of numbers that will be compared.</summary>
        public int[] Data;

        /// <summary>
        /// Array of booleans that flag for modified data.
        /// This is the result of the diff.
        /// This means deletedA in the first Data or inserted in the second Data.
        /// </summary>
        public bool[] Modified;

        /// <summary>
        /// Initialize the Diff-Data buffer.
        /// </summary>
        /// <param name="data">reference to the buffer</param>
        public DiffData(int[] initData)
        {
            Data = initData;
            Modified = new bool[Length + 2];
        }
    }
}