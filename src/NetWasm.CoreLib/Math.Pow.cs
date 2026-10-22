// Managed port of FreeBSD msun e_pow.c as carried by Emscripten 6.0.7 in
// system/lib/libc/musl/src/math/pow_small.c, the pow of its size-optimized libc.
// Adaptations: C# bit access, the two-entry bp, dp_h and dp_l tables read as k == 0 conditionals
// (a method-local span made each call about five times slower in NetWasm), Math.Sqrt and
// Math.ScaleB for sqrt and scalbn, and no floating-point exception side effects.
/* origin: FreeBSD /usr/src/lib/msun/src/e_pow.c */
/*
 * ====================================================
 * Copyright (C) 2004 by Sun Microsystems, Inc. All rights reserved.
 *
 * Permission to use, copy, modify, and distribute this
 * software is freely granted, provided that this notice
 * is preserved.
 * ====================================================
 */
/* pow(x,y) return x**y
 *
 *                    n
 * Method:  Let x =  2   * (1+f)
 *      1. Compute and return log2(x) in two pieces:
 *              log2(x) = w1 + w2,
 *         where w1 has 53-24 = 29 bit trailing zeros.
 *      2. Perform y*log2(x) = n+y' by simulating muti-precision
 *         arithmetic, where |y'|<=0.5.
 *      3. Return x**y = 2**n*exp(y'*log2)
 *
 * Special cases:
 *      1.  (anything) ** 0  is 1
 *      2.  1 ** (anything)  is 1
 *      3.  (anything except 1) ** NAN is NAN
 *      4.  NAN ** (anything except 0) is NAN
 *      5.  +-(|x| > 1) **  +INF is +INF
 *      6.  +-(|x| > 1) **  -INF is +0
 *      7.  +-(|x| < 1) **  +INF is +0
 *      8.  +-(|x| < 1) **  -INF is +INF
 *      9.  -1          ** +-INF is 1
 *      10. +0 ** (+anything except 0, NAN)               is +0
 *      11. -0 ** (+anything except 0, NAN, odd integer)  is +0
 *      12. +0 ** (-anything except 0, NAN)               is +INF, raise divbyzero
 *      13. -0 ** (-anything except 0, NAN, odd integer)  is +INF, raise divbyzero
 *      14. -0 ** (+odd integer) is -0
 *      15. -0 ** (-odd integer) is -INF, raise divbyzero
 *      16. +INF ** (+anything except 0,NAN) is +INF
 *      17. +INF ** (-anything except 0,NAN) is +0
 *      18. -INF ** (+odd integer) is -INF
 *      19. -INF ** (anything) = -0 ** (-anything), (anything except odd integer)
 *      20. (anything) ** 1 is (anything)
 *      21. (anything) ** -1 is 1/(anything)
 *      22. (-anything) ** (integer) is (-1)**(integer)*(+anything**integer)
 *      23. (-anything except 0 and inf) ** (non-integer) is NAN
 *
 * Accuracy:
 *      pow(x,y) returns x**y nearly rounded. In particular
 *                      pow(integer,integer)
 *      always returns the correct integer provided it is
 *      representable.
 *
 * Constants :
 * The hexadecimal values are the intended ones for the following
 * constants. The decimal values may be used, provided that the
 * compiler will convert from decimal to binary accurately enough
 * to produce the hexadecimal values shown.
 */

namespace System;

public static partial class Math
{
    public static double Pow(double x, double y)
    {
        const double bp0 = 1.0, bp1 = 1.5;
        const double dp_h0 = 0.0, dp_h1 = 5.84962487220764160156e-01; /* 0x3FE2B803, 0x40000000 */
        const double dp_l0 = 0.0, dp_l1 = 1.35003920212974897128e-08; /* 0x3E4CFDEB, 0x43CFD006 */
        const double two53 = 9007199254740992.0; /* 0x43400000, 0x00000000 */
        const double huge = 1.0e300;
        const double tiny = 1.0e-300;
        /* poly coefs for (3/2)*(log(x)-2s-2/3*s**3 */
        const double L1 = 5.99999999999994648725e-01; /* 0x3FE33333, 0x33333303 */
        const double L2 = 4.28571428578550184252e-01; /* 0x3FDB6DB6, 0xDB6FABFF */
        const double L3 = 3.33333329818377432918e-01; /* 0x3FD55555, 0x518F264D */
        const double L4 = 2.72728123808534006489e-01; /* 0x3FD17460, 0xA91D4101 */
        const double L5 = 2.30660745775561754067e-01; /* 0x3FCD864A, 0x93C9DB65 */
        const double L6 = 2.06975017800338417784e-01; /* 0x3FCA7E28, 0x4A454EEF */
        const double P1 = 1.66666666666666019037e-01; /* 0x3FC55555, 0x5555553E */
        const double P2 = -2.77777777770155933842e-03; /* 0xBF66C16C, 0x16BEBD93 */
        const double P3 = 6.61375632143793436117e-05; /* 0x3F11566A, 0xAF25DE2C */
        const double P4 = -1.65339022054652515390e-06; /* 0xBEBBBD41, 0xC5D26BF1 */
        const double P5 = 4.13813679705723846039e-08; /* 0x3E663769, 0x72BEA4D0 */
        const double lg2 = 6.93147180559945286227e-01; /* 0x3FE62E42, 0xFEFA39EF */
        const double lg2_h = 6.93147182464599609375e-01; /* 0x3FE62E43, 0x00000000 */
        const double lg2_l = -1.90465429995776804525e-09; /* 0xBE205C61, 0x0CA86C39 */
        const double ovt = 8.0085662595372944372e-017; /* -(1024-log2(ovfl+.5ulp)) */
        const double cp = 9.61796693925975554329e-01; /* 0x3FEEC709, 0xDC3A03FD =2/(3ln2) */
        const double cp_h = 9.61796700954437255859e-01; /* 0x3FEEC709, 0xE0000000 =(float)cp */
        const double cp_l = -7.02846165095275826516e-09; /* 0xBE3E2FE0, 0x145B01F5 =tail of cp_h*/
        const double ivln2 = 1.44269504088896338700e+00; /* 0x3FF71547, 0x652B82FE =1/ln2 */
        const double ivln2_h = 1.44269502162933349609e+00; /* 0x3FF71547, 0x60000000 =24b 1/ln2*/
        const double ivln2_l = 1.92596299112661746887e-08; /* 0x3E54AE0B, 0xF85DDF44 =1/ln2 tail*/

        double z, ax, z_h, z_l, p_h, p_l;
        double y1, t1, t2, r, s, t, u, v, w;
        int i, j, k, yisint, n;
        int hx, hy, ix, iy;
        uint lx, ly;

        hx = FdHigh(x);
        lx = (uint)FdLow(x);
        hy = FdHigh(y);
        ly = (uint)FdLow(y);
        ix = hx & 0x7fffffff;
        iy = hy & 0x7fffffff;

        /* x**0 = 1, even if x is NaN */
        if (((uint)iy | ly) == 0)
            return 1.0;
        /* 1**y = 1, even if y is NaN */
        if (hx == 0x3ff00000 && lx == 0)
            return 1.0;
        /* NaN if either arg is NaN */
        if (ix > 0x7ff00000 || (ix == 0x7ff00000 && lx != 0) ||
            iy > 0x7ff00000 || (iy == 0x7ff00000 && ly != 0))
            return x + y;

        /* determine if y is an odd int when x < 0
         * yisint = 0       ... y is not an integer
         * yisint = 1       ... y is an odd int
         * yisint = 2       ... y is an even int
         */
        yisint = 0;
        if (hx < 0)
        {
            if (iy >= 0x43400000)
                yisint = 2; /* even integer y */
            else if (iy >= 0x3ff00000)
            {
                k = (iy >> 20) - 0x3ff;  /* exponent */
                if (k > 20)
                {
                    j = (int)(ly >> (52 - k));
                    if ((uint)(j << (52 - k)) == ly)
                        yisint = 2 - (j & 1);
                }
                else if (ly == 0)
                {
                    j = iy >> (20 - k);
                    if ((j << (20 - k)) == iy)
                        yisint = 2 - (j & 1);
                }
            }
        }

        /* special value of y */
        if (ly == 0)
        {
            if (iy == 0x7ff00000)
            {  /* y is +-inf */
                if (((ix - 0x3ff00000) | (int)lx) == 0)  /* (-1)**+-inf is 1 */
                    return 1.0;
                else if (ix >= 0x3ff00000) /* (|x|>1)**+-inf = inf,0 */
                    return hy >= 0 ? y : 0.0;
                else                       /* (|x|<1)**+-inf = 0,inf */
                    return hy >= 0 ? 0.0 : -y;
            }
            if (iy == 0x3ff00000)
            {    /* y is +-1 */
                if (hy >= 0)
                    return x;
                y = 1 / x;
                return y;
            }
            if (hy == 0x40000000)    /* y is 2 */
                return x * x;
            if (hy == 0x3fe00000)
            {  /* y is 0.5 */
                if (hx >= 0)     /* x >= +0 */
                    return Sqrt(x);
            }
        }

        ax = Abs(x);
        /* special value of x */
        if (lx == 0)
        {
            if (ix == 0x7ff00000 || ix == 0 || ix == 0x3ff00000)
            { /* x is +-0,+-inf,+-1 */
                z = ax;
                if (hy < 0)   /* z = (1/|x|) */
                    z = 1.0 / z;
                if (hx < 0)
                {
                    if (((ix - 0x3ff00000) | yisint) == 0)
                    {
                        z = (z - z) / (z - z); /* (-1)**non-int is NaN */
                    }
                    else if (yisint == 1)
                        z = -z;          /* (x<0)**odd = -(|x|**odd) */
                }
                return z;
            }
        }

        s = 1.0; /* sign of result */
        if (hx < 0)
        {
            if (yisint == 0) /* (x<0)**(non-int) is NaN */
                return (x - x) / (x - x);
            if (yisint == 1) /* (x<0)**(odd int) */
                s = -1.0;
        }

        /* |y| is huge */
        if (iy > 0x41e00000)
        { /* if |y| > 2**31 */
            if (iy > 0x43f00000)
            {  /* if |y| > 2**64, must o/uflow */
                if (ix <= 0x3fefffff)
                    return hy < 0 ? huge * huge : tiny * tiny;
                if (ix >= 0x3ff00000)
                    return hy > 0 ? huge * huge : tiny * tiny;
            }
            /* over/underflow if x is not close to one */
            if (ix < 0x3fefffff)
                return hy < 0 ? s * huge * huge : s * tiny * tiny;
            if (ix > 0x3ff00000)
                return hy > 0 ? s * huge * huge : s * tiny * tiny;
            /* now |1-x| is tiny <= 2**-20, suffice to compute
               log(x) by x-x^2/2+x^3/3-x^4/4 */
            t = ax - 1.0;       /* t has 20 trailing zeros */
            w = (t * t) * (0.5 - t * (0.3333333333333333333333 - t * 0.25));
            u = ivln2_h * t;      /* ivln2_h has 21 sig. bits */
            v = t * ivln2_l - w * ivln2;
            t1 = u + v;
            t1 = FdWithLow(t1, 0);
            t2 = v - (t1 - u);
        }
        else
        {
            double ss, s2, s_h, s_l, t_h, t_l;
            n = 0;
            /* take care subnormal number */
            if (ix < 0x00100000)
            {
                ax *= two53;
                n -= 53;
                ix = FdHigh(ax);
            }
            n += ((ix) >> 20) - 0x3ff;
            j = ix & 0x000fffff;
            /* determine interval */
            ix = j | 0x3ff00000;   /* normalize ix */
            if (j <= 0x3988E)      /* |x|<sqrt(3/2) */
                k = 0;
            else if (j < 0xBB67A)  /* |x|<sqrt(3)   */
                k = 1;
            else
            {
                k = 0;
                n += 1;
                ix -= 0x00100000;
            }
            ax = FdWithHigh(ax, ix);

            /* compute ss = s_h+s_l = (x-1)/(x+1) or (x-1.5)/(x+1.5) */
            u = ax - (k == 0 ? bp0 : bp1);        /* bp[0]=1.0, bp[1]=1.5 */
            v = 1.0 / (ax + (k == 0 ? bp0 : bp1));
            ss = u * v;
            s_h = ss;
            s_h = FdWithLow(s_h, 0);
            /* t_h=ax+bp[k] High */
            t_h = 0.0;
            t_h = FdWithHigh(t_h, ((ix >> 1) | 0x20000000) + 0x00080000 + (k << 18));
            t_l = ax - (t_h - (k == 0 ? bp0 : bp1));
            s_l = v * ((u - s_h * t_h) - s_h * t_l);
            /* compute log(ax) */
            s2 = ss * ss;
            r = s2 * s2 * (L1 + s2 * (L2 + s2 * (L3 + s2 * (L4 + s2 * (L5 + s2 * L6)))));
            r += s_l * (s_h + ss);
            s2 = s_h * s_h;
            t_h = 3.0 + s2 + r;
            t_h = FdWithLow(t_h, 0);
            t_l = r - ((t_h - 3.0) - s2);
            /* u+v = ss*(1+...) */
            u = s_h * t_h;
            v = s_l * t_h + t_l * ss;
            /* 2/(3log2)*(ss+...) */
            p_h = u + v;
            p_h = FdWithLow(p_h, 0);
            p_l = v - (p_h - u);
            z_h = cp_h * p_h;        /* cp_h+cp_l = 2/(3*log2) */
            z_l = cp_l * p_h + p_l * cp + (k == 0 ? dp_l0 : dp_l1);
            /* log2(ax) = (ss+..)*2/(3*log2) = n + dp_h + z_h + z_l */
            t = (double)n;
            t1 = ((z_h + z_l) + (k == 0 ? dp_h0 : dp_h1)) + t;
            t1 = FdWithLow(t1, 0);
            t2 = z_l - (((t1 - t) - (k == 0 ? dp_h0 : dp_h1)) - z_h);
        }

        /* split up y into y1+y2 and compute (y1+y2)*(t1+t2) */
        y1 = y;
        y1 = FdWithLow(y1, 0);
        p_l = (y - y1) * t1 + y * t2;
        p_h = y1 * t1;
        z = p_l + p_h;
        j = FdHigh(z);
        i = FdLow(z);
        if (j >= 0x40900000)
        {                      /* z >= 1024 */
            if (((j - 0x40900000) | i) != 0)        /* if z > 1024 */
                return s * huge * huge;         /* overflow */
            if (p_l + ovt > z - p_h)
                return s * huge * huge;         /* overflow */
        }
        else if ((j & 0x7fffffff) >= 0x4090cc00)
        {  /* z <= -1075 */  // FIXME: instead of abs(j) use unsigned j
            if ((((uint)j - 0xc090cc00U) | (uint)i) != 0)        /* z < -1075 */
                return s * tiny * tiny;         /* underflow */
            if (p_l <= z - p_h)
                return s * tiny * tiny;         /* underflow */
        }
        /*
         * compute 2**(p_h+p_l)
         */
        i = j & 0x7fffffff;
        k = (i >> 20) - 0x3ff;
        n = 0;
        if (i > 0x3fe00000)
        {  /* if |z| > 0.5, set n = [z+0.5] */
            n = j + (0x00100000 >> (k + 1));
            k = ((n & 0x7fffffff) >> 20) - 0x3ff;  /* new k for n */
            t = 0.0;
            t = FdWithHigh(t, n & ~(0x000fffff >> k));
            n = ((n & 0x000fffff) | 0x00100000) >> (20 - k);
            if (j < 0)
                n = -n;
            p_h -= t;
        }
        t = p_l + p_h;
        t = FdWithLow(t, 0);
        u = t * lg2_h;
        v = (p_l - (t - p_h)) * lg2 + t * lg2_l;
        z = u + v;
        w = v - (z - u);
        t = z * z;
        t1 = z - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
        r = (z * t1) / (t1 - 2.0) - (w + z * w);
        z = 1.0 - (r - z);
        j = FdHigh(z);
        j += n << 20;
        if ((j >> 20) <= 0)  /* subnormal output */
            z = ScaleB(z, n);
        else
            z = FdWithHigh(z, j);
        return s * z;
    }
}
