using System;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Math.EC.Multiplier;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;

namespace Org.BouncyCastle.Crypto.Signers
{
    /**
     * GOST R 34.10-2001 Signature Algorithm
     */
    public class ECGost3410Signer
        : IDsa
    {
        private ECKeyParameters key;
        private SecureRandom random;
        private bool forSigning;

        public virtual string AlgorithmName
        {
            get { return key.AlgorithmName; }
        }

        public virtual void Init(bool forSigning, ICipherParameters parameters)
        {
            this.forSigning = forSigning;

            if (forSigning)
            {
                parameters = ParameterUtilities.GetRandom(parameters, out var providedRandom);

                if (!(parameters is ECPrivateKeyParameters ecPrivateKeyParameters))
                    throw new InvalidKeyException("EC private key required for signing");

                this.key = ecPrivateKeyParameters;
                this.random = CryptoServicesRegistrar.GetSecureRandom(providedRandom);
            }
            else
            {
                if (!(parameters is ECPublicKeyParameters ecPublicKeyParameters))
                    throw new InvalidKeyException("EC public key required for verification");

                this.key = ecPublicKeyParameters;
                this.random = null;
            }
        }

        public virtual BigInteger Order => key.Parameters.N;

        /**
         * generate a signature for the given message using the key we were
         * initialised with. For conventional GOST3410 the message should be a GOST3411
         * hash of the message of interest.
         *
         * @param message the message that will be verified later.
         */
        public virtual BigInteger[] GenerateSignature(byte[] message)
        {
            if (!forSigning)
                throw new InvalidOperationException("not initialized for signing");

            BigInteger e = new BigInteger(1, message, bigEndian: false);

            ECDomainParameters ec = key.Parameters;
            BigInteger n = ec.N;
            BigInteger d = ((ECPrivateKeyParameters)key).D;

            BigInteger r, s;

            ECMultiplier basePointMultiplier = CreateBasePointMultiplier();

            do // generate s
            {
                BigInteger k;
                do // generate r
                {
                    do
                    {
                        k = BigIntegers.CreateRandomBigInteger(n.BitLength, random);
                    }
                    while (k.SignValue == 0);

                    ECPoint p = basePointMultiplier.Multiply(ec.G, k).Normalize();

                    r = p.AffineXCoord.ToBigInteger().Mod(n);
                }
                while (r.SignValue == 0);

                s = k.Multiply(e).Add(d.Multiply(r)).Mod(n);
            }
            while (s.SignValue == 0);

            return new BigInteger[]{ r, s };
        }

        /**
         * return true if the value r and s represent a GOST3410 signature for
         * the passed in message (for standard GOST3410 the message should be
         * a GOST3411 hash of the real message to be verified).
         */
        public virtual bool VerifySignature(byte[] message, BigInteger r, BigInteger s)
        {
            if (forSigning)
                throw new InvalidOperationException("not initialized for verification");

            BigInteger e = new BigInteger(1, message, bigEndian: false);
            BigInteger n = key.Parameters.N;

            // r in the range [1,n-1]
            if (r.CompareTo(BigInteger.One) < 0 || r.CompareTo(n) >= 0)
                return false;

            // s in the range [1,n-1]
            if (s.CompareTo(BigInteger.One) < 0 || s.CompareTo(n) >= 0)
                return false;

            BigInteger v = BigIntegers.ModOddInverseVar(n, e);

            BigInteger z1 = s.Multiply(v).Mod(n);
            BigInteger z2 = n.Subtract(r).Multiply(v).Mod(n);

            ECPoint G = key.Parameters.G; // P
            ECPoint Q = ((ECPublicKeyParameters)key).Q;

            ECPoint point = ECAlgorithms.SumOfTwoMultiplies(G, z1, Q, z2).Normalize();

            if (point.IsInfinity)
                return false;

            BigInteger R = point.AffineXCoord.ToBigInteger().Mod(n);

            return R.Equals(r);
        }

        protected virtual ECMultiplier CreateBasePointMultiplier()
        {
            return new FixedPointCombMultiplier();
        }
    }
}
