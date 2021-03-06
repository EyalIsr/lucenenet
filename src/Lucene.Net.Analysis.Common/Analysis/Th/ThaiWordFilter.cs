﻿using ICU4NET;
using ICU4NETExtension;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Tokenattributes;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Util;
using System;
using System.Text.RegularExpressions;

namespace Lucene.Net.Analysis.Th
{
    /*
	 * Licensed to the Apache Software Foundation (ASF) under one or more
	 * contributor license agreements.  See the NOTICE file distributed with
	 * this work for additional information regarding copyright ownership.
	 * The ASF licenses this file to You under the Apache License, Version 2.0
	 * (the "License"); you may not use this file except in compliance with
	 * the License.  You may obtain a copy of the License at
	 *
	 *     http://www.apache.org/licenses/LICENSE-2.0
	 *
	 * Unless required by applicable law or agreed to in writing, software
	 * distributed under the License is distributed on an "AS IS" BASIS,
	 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	 * See the License for the specific language governing permissions and
	 * limitations under the License.
	 */

    /// <summary>
    /// <seealso cref="TokenFilter"/> that use <seealso cref="java.text.BreakIterator"/> to break each 
    /// Token that is Thai into separate Token(s) for each Thai word.
    /// <para>Please note: Since matchVersion 3.1 on, this filter no longer lowercases non-thai text.
    /// <seealso cref="ThaiAnalyzer"/> will insert a <seealso cref="LowerCaseFilter"/> before this filter
    /// so the behaviour of the Analyzer does not change. With version 3.1, the filter handles
    /// position increments correctly.
    /// </para>
    /// <para>WARNING: this filter may not be supported by all JREs.
    ///    It is known to work with Sun/Oracle and Harmony JREs.
    ///    If your application needs to be fully portable, consider using ICUTokenizer instead,
    ///    which uses an ICU Thai BreakIterator that will always be available.
    /// </para>
    /// </summary>
    /// @deprecated Use <seealso cref="ThaiTokenizer"/> instead. 
    [Obsolete("Use ThaiTokenizer instead.")]
    public sealed class ThaiWordFilter : TokenFilter
    {
        /// <summary>
        /// True if the JRE supports a working dictionary-based breakiterator for Thai.
        /// If this is false, this filter will not work at all!
        /// </summary>
        public static readonly bool DBBI_AVAILABLE = ThaiTokenizer.DBBI_AVAILABLE;
        private readonly ThaiWordBreaker breaker = new ThaiWordBreaker(BreakIterator.CreateWordInstance(new Locale()));
        private readonly CharArrayIterator charIterator = CharArrayIterator.NewWordInstance();

        private readonly bool handlePosIncr;

        private readonly ICharTermAttribute termAtt;
        private readonly IOffsetAttribute offsetAtt;
        private readonly IPositionIncrementAttribute posAtt;

        private AttributeSource clonedToken = null;
        private ICharTermAttribute clonedTermAtt = null;
        private IOffsetAttribute clonedOffsetAtt = null;
        private bool hasMoreTokensInClone = false;
        private bool hasIllegalOffsets = false; // only if the length changed before this filter

        /// <summary>
        /// Creates a new ThaiWordFilter with the specified match version. </summary>
        public ThaiWordFilter(LuceneVersion matchVersion, TokenStream input)
              : base(matchVersion.OnOrAfter(LuceneVersion.LUCENE_31) ? input : new LowerCaseFilter(matchVersion, input))
        {
            if (!DBBI_AVAILABLE)
            {
                throw new System.NotSupportedException("This JRE does not have support for Thai segmentation");
            }
            handlePosIncr = matchVersion.OnOrAfter(LuceneVersion.LUCENE_31);
            termAtt = AddAttribute<ICharTermAttribute>();
            offsetAtt = AddAttribute<IOffsetAttribute>();
            posAtt = AddAttribute<IPositionIncrementAttribute>();
        }

        public override bool IncrementToken()
        {
            if (hasMoreTokensInClone)
            {
                int start = breaker.Current();
                int end = breaker.Next();
                if (end != BreakIterator.DONE)
                {
                    clonedToken.CopyTo(this);
                    termAtt.CopyBuffer(clonedTermAtt.Buffer(), start, end - start);
                    if (hasIllegalOffsets)
                    {
                        offsetAtt.SetOffset(clonedOffsetAtt.StartOffset(), clonedOffsetAtt.EndOffset());
                    }
                    else
                    {
                        offsetAtt.SetOffset(clonedOffsetAtt.StartOffset() + start, clonedOffsetAtt.StartOffset() + end);
                    }
                    if (handlePosIncr)
                    {
                        posAtt.PositionIncrement = 1;
                    }
                    return true;
                }
                hasMoreTokensInClone = false;
            }

            if (!input.IncrementToken())
            {
                return false;
            }

            if (termAtt.Length == 0 || !Regex.IsMatch(termAtt.ToString().Substring(0, 1), @"\p{IsThai}"))
            {
                return true;
            }

            hasMoreTokensInClone = true;

            // if length by start + end offsets doesn't match the term text then assume
            // this is a synonym and don't adjust the offsets.
            hasIllegalOffsets = offsetAtt.EndOffset() - offsetAtt.StartOffset() != termAtt.Length;

            // we lazy init the cloned token, as in ctor not all attributes may be added
            if (clonedToken == null)
            {
                clonedToken = CloneAttributes();
                clonedTermAtt = clonedToken.GetAttribute<ICharTermAttribute>();
                clonedOffsetAtt = clonedToken.GetAttribute<IOffsetAttribute>();
            }
            else
            {
                this.CopyTo(clonedToken);
            }

            // reinit CharacterIterator
            charIterator.SetText(clonedTermAtt.Buffer(), 0, clonedTermAtt.Length);
            breaker.SetText(new string(charIterator.Text, charIterator.Start, charIterator.Length));
            int end2 = breaker.Next();
            if (end2 != BreakIterator.DONE)
            {
                termAtt.Length = end2;
                if (hasIllegalOffsets)
                {
                    offsetAtt.SetOffset(clonedOffsetAtt.StartOffset(), clonedOffsetAtt.EndOffset());
                }
                else
                {
                    offsetAtt.SetOffset(clonedOffsetAtt.StartOffset(), clonedOffsetAtt.StartOffset() + end2);
                }
                // position increment keeps as it is for first token
                return true;
            }
            return false;
        }

        public override void Reset()
        {
            base.Reset();
            hasMoreTokensInClone = false;
            clonedToken = null;
            clonedTermAtt = null;
            clonedOffsetAtt = null;
        }
    }
}