namespace ArctisAurora.Core.Tex
{
    // The LaTeX commands that are plain macros over the typesetter's primitives, read before every source.
    public static class TexFormat
    {
        public const string Prelude = """
            \makeatletter
            \newcounter{part}\newcounter{chapter}\newcounter{section}[chapter]%
            \newcounter{subsection}[section]\newcounter{subsubsection}[subsection]%
            \newcounter{paragraph}[subsubsection]\newcounter{subparagraph}[paragraph]%
            \newcounter{footnote}\newcounter{figure}[chapter]\newcounter{table}[chapter]%
            \dimendef\textwidth=250 \dimendef\linewidth=251 \dimendef\columnwidth=252 \dimendef\textheight=253 %
            \dimendef\paperwidth=254 \dimendef\paperheight=255 %
            \def\figurename{Figure}\def\tablename{Table}\def\refname{References}\def\bibname{Bibliography}%
            \def\newblock{\hskip .11em plus .33em minus .07em\relax}%
            \newcommand\textbf[1]{{\bfseries#1}}\newcommand\textmd[1]{{\mdseries#1}}%
            \newcommand\textit[1]{{\itshape#1}}\newcommand\textsl[1]{{\slshape#1}}%
            \newcommand\textsc[1]{{\scshape#1}}\newcommand\textup[1]{{\upshape#1}}%
            \newcommand\textrm[1]{{\rmfamily#1}}\newcommand\textsf[1]{{\sffamily#1}}%
            \newcommand\texttt[1]{{\ttfamily#1}}\newcommand\textnormal[1]{{\normalfont#1}}%
            \newcommand\emph[1]{{\em#1}}\newcommand\underline[1]{{\@underline#1}}%
            \newcommand\textcolor[3][]{{\color[#1]{#2}#3}}%
            \def\bf{\normalfont\bfseries}\def\it{\normalfont\itshape}\def\sl{\normalfont\slshape}%
            \def\sc{\normalfont\scshape}\def\tt{\normalfont\ttfamily}\def\rm{\normalfont\rmfamily}%
            \def\sf{\normalfont\sffamily}%
            \def\title#1{\gdef\@title{#1}}\def\author#1{\gdef\@author{#1}}\def\date#1{\gdef\@date{#1}}%
            \def\@title{}\def\@author{}\def\@date{\today}\def\and{\quad}\def\thanks#1{}%
            \def\maketitle{\par\begingroup\centering\LARGE\@title\par\large\@author\par\@date\par\endgroup}%
            \def\pageref#1{??}\def\eqref#1{(??)}%
            \newcommand\hspace{\@ifstar\@hspace\@hspace}\def\@hspace#1{\hskip#1\relax}%
            \newcommand\vspace{\@ifstar\@vspace\@vspace}\def\@vspace#1{\vskip#1\relax}%
            \def\hfill{\hskip0pt plus1fill\relax}\def\vfill{\vskip0pt plus1fill\relax}%
            \def\smallskip{\vskip3pt plus1pt minus1pt\relax}\def\medskip{\vskip6pt plus2pt minus2pt\relax}%
            \def\bigskip{\vskip12pt plus4pt minus4pt\relax}%
            \def\newline{\\}\newcommand\linebreak[1][]{\\}\newcommand\pagebreak[1][]{\par}%
            \def\newpage{\par}\def\clearpage{\par}\def\cleardoublepage{\par}%
            \def\enspace{\kern.5em}\def\thinspace{\kern.16667em}\def\negthinspace{\kern-.16667em}%
            \def~{\nobreakspace}\def\-{}\def\/{}\def\@{}%
            \@pagedimens
            \makeatother
            """;
    }
}
