using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.EdEC;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Utilities.IO;

namespace Org.BouncyCastle.Bcpg.OpenPgp
{
    /// <remarks>Basic utility class.</remarks>
    public sealed class PgpUtilities
    {
        private static readonly S2k LegacyS2k = new S2k(HashAlgorithmTag.MD5);

        private static readonly Dictionary<string, HashAlgorithmTag> NameToHashID = CreateNameToHashID();
        private static readonly Dictionary<DerObjectIdentifier, string> OidToName = CreateOidToName();

        private static Dictionary<string, HashAlgorithmTag> CreateNameToHashID()
        {
            var d = new Dictionary<string, HashAlgorithmTag>(StringComparer.OrdinalIgnoreCase);
            d.Add("sha1", HashAlgorithmTag.Sha1);
            d.Add("sha224", HashAlgorithmTag.Sha224);
            d.Add("sha256", HashAlgorithmTag.Sha256);
            d.Add("sha384", HashAlgorithmTag.Sha384);
            d.Add("sha512", HashAlgorithmTag.Sha512);
            d.Add("ripemd160", HashAlgorithmTag.RipeMD160);
            d.Add("rmd160", HashAlgorithmTag.RipeMD160);
            d.Add("md2", HashAlgorithmTag.MD2);
            d.Add("tiger", HashAlgorithmTag.Tiger192);
            d.Add("haval", HashAlgorithmTag.Haval5pass160);
            d.Add("md5", HashAlgorithmTag.MD5);
            return d;
        }

        private static Dictionary<DerObjectIdentifier, string> CreateOidToName()
        {
            var d = new Dictionary<DerObjectIdentifier, string>();
            d.Add(EdECObjectIdentifiers.id_X25519, "Curve25519");
            d.Add(EdECObjectIdentifiers.id_Ed25519, "Ed25519");
            d.Add(SecObjectIdentifiers.SecP256r1, "NIST P-256");
            d.Add(SecObjectIdentifiers.SecP384r1, "NIST P-384");
            d.Add(SecObjectIdentifiers.SecP521r1, "NIST P-521");
            return d;
        }

        public static MPInteger[] DsaSigToMpi(byte[] encoding)
        {
            try
            {
                Asn1Sequence s = Asn1Sequence.GetInstance(encoding);

                var i1 = DerInteger.GetInstance(s[0]);
                var i2 = DerInteger.GetInstance(s[1]);

                return new MPInteger[]{ new MPInteger(i1.Value), new MPInteger(i2.Value) };
            }
            catch (Exception e)
            {
                throw new PgpException("exception encoding signature", e);
            }
        }

        public static MPInteger[] RsaSigToMpi(byte[] encoding) =>
            new MPInteger[]{ new MPInteger(new BigInteger(1, encoding)) };

        public static string GetDigestName(HashAlgorithmTag hashAlgorithm)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            switch (hashAlgorithm)
            {
            case HashAlgorithmTag.Sha1:
                return "SHA1";
            case HashAlgorithmTag.MD2:
                return "MD2";
            case HashAlgorithmTag.MD5:
                return "MD5";
            case HashAlgorithmTag.RipeMD160:
                return "RIPEMD160";
            case HashAlgorithmTag.Sha224:
                return "SHA224";
            case HashAlgorithmTag.Sha256:
                return "SHA256";
            case HashAlgorithmTag.Sha384:
                return "SHA384";
            case HashAlgorithmTag.Sha512:
                return "SHA512";
            case HashAlgorithmTag.Sha3_256:
            case HashAlgorithmTag.Sha3_256_Old:
                return "SHA3-256";
            case HashAlgorithmTag.Sha3_384:
                return "SHA3-384";
            case HashAlgorithmTag.Sha3_512:
            case HashAlgorithmTag.Sha3_512_Old:
                return "SHA3-512";
            case HashAlgorithmTag.Sha3_224:
                return "SHA3-224";
            case HashAlgorithmTag.Tiger192:
                return "TIGER";
            default:
                throw new PgpException("unknown hash algorithm tag in GetDigestName: " + hashAlgorithm);
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public static int GetDigestIDForName(string name)
        {
            if (NameToHashID.TryGetValue(name, out var hashAlgorithmTag))
                return (int)hashAlgorithmTag;

            throw new ArgumentException("unable to map " + name + " to a hash id", nameof(name));
        }

        /**
         * Return the EC curve name for the passed in OID.
         *
         * @param oid the EC curve object identifier in the PGP key
         * @return  a string representation of the OID.
         */
        public static string GetCurveName(DerObjectIdentifier oid)
        {
            if (OidToName.TryGetValue(oid, out var name))
                return name;

            // fall back
            return ECNamedCurveTable.GetName(oid);
        }

        public static string GetSignatureName(PublicKeyAlgorithmTag keyAlgorithm, HashAlgorithmTag hashAlgorithm)
        {
            string encAlg;
            switch (keyAlgorithm)
            {
            case PublicKeyAlgorithmTag.RsaGeneral:
            case PublicKeyAlgorithmTag.RsaSign:
                encAlg = "RSA";
                break;
            case PublicKeyAlgorithmTag.Dsa:
                encAlg = "DSA";
                break;
            case PublicKeyAlgorithmTag.ECDH:
                encAlg = "ECDH";
                break;
            case PublicKeyAlgorithmTag.ECDsa:
                encAlg = "ECDSA";
                break;
            case PublicKeyAlgorithmTag.EdDsa_Legacy:
                encAlg = "EdDSA";
                break;
            case PublicKeyAlgorithmTag.ElGamalEncrypt: // in some malformed cases.
            case PublicKeyAlgorithmTag.ElGamalGeneral:
                encAlg = "ElGamal";
                break;
            default:
                throw new PgpException("unknown algorithm tag in signature:" + keyAlgorithm);
            }

            return GetDigestName(hashAlgorithm) + "with" + encAlg;
        }

        public static string GetSymmetricCipherName(SymmetricKeyAlgorithmTag algorithm)
        {
            switch (algorithm)
            {
            case SymmetricKeyAlgorithmTag.Null:
                return null;
            case SymmetricKeyAlgorithmTag.TripleDes:
                return "DESEDE";
            case SymmetricKeyAlgorithmTag.Idea:
                return "IDEA";
            case SymmetricKeyAlgorithmTag.Cast5:
                return "CAST5";
            case SymmetricKeyAlgorithmTag.Blowfish:
                return "Blowfish";
            case SymmetricKeyAlgorithmTag.Safer:
                return "SAFER";
            case SymmetricKeyAlgorithmTag.Des:
                return "DES";
            case SymmetricKeyAlgorithmTag.Aes128:
                return "AES";
            case SymmetricKeyAlgorithmTag.Aes192:
                return "AES";
            case SymmetricKeyAlgorithmTag.Aes256:
                return "AES";
            case SymmetricKeyAlgorithmTag.Twofish:
                return "Twofish";
            case SymmetricKeyAlgorithmTag.Camellia128:
                return "Camellia";
            case SymmetricKeyAlgorithmTag.Camellia192:
                return "Camellia";
            case SymmetricKeyAlgorithmTag.Camellia256:
                return "Camellia";
            default:
                throw new PgpException("unknown symmetric algorithm: " + algorithm);
            }
        }

        public static int GetKeySize(SymmetricKeyAlgorithmTag algorithm)
        {
            int keySize;
            switch (algorithm)
            {
            case SymmetricKeyAlgorithmTag.Des:
                keySize = 64;
                break;
            case SymmetricKeyAlgorithmTag.Idea:
            case SymmetricKeyAlgorithmTag.Cast5:
            case SymmetricKeyAlgorithmTag.Blowfish:
            case SymmetricKeyAlgorithmTag.Safer:
            case SymmetricKeyAlgorithmTag.Aes128:
            case SymmetricKeyAlgorithmTag.Camellia128:
                keySize = 128;
                break;
            case SymmetricKeyAlgorithmTag.TripleDes:
            case SymmetricKeyAlgorithmTag.Aes192:
            case SymmetricKeyAlgorithmTag.Camellia192:
                keySize = 192;
                break;
            case SymmetricKeyAlgorithmTag.Aes256:
            case SymmetricKeyAlgorithmTag.Twofish:
            case SymmetricKeyAlgorithmTag.Camellia256:
                keySize = 256;
                break;
            default:
                throw new PgpException("unknown symmetric algorithm: " + algorithm);
            }

            return keySize;
        }

        public static KeyParameter MakeKey(SymmetricKeyAlgorithmTag algorithm, byte[] keyBytes)
        {
            string algName = GetSymmetricCipherName(algorithm);

            return ParameterUtilities.CreateKeyParameter(algName, keyBytes);
        }

        public static KeyParameter MakeRandomKey(SymmetricKeyAlgorithmTag algorithm, SecureRandom random)
        {
            int keySize = GetKeySize(algorithm);
            byte[] keyBytes = new byte[(keySize + 7) / 8];
            random.NextBytes(keyBytes);
            return MakeKey(algorithm, keyBytes);
        }

        internal static byte[] EncodePassPhrase(char[] passPhrase, bool utf8)
        {
            return passPhrase == null
                ? null
                : utf8
                ? Encoding.UTF8.GetBytes(passPhrase)
                : Strings.ToByteArray(passPhrase);
        }

        /// <remarks>
        /// Conversion of the passphrase characters to bytes is performed using Convert.ToByte(), which is
        /// the historical behaviour of the library (1.7 and earlier).
        /// </remarks>
        public static KeyParameter MakeKeyFromPassPhrase(SymmetricKeyAlgorithmTag algorithm, S2k s2k, char[] passPhrase)
        {
            return DoMakeKeyFromPassPhrase(algorithm, s2k, EncodePassPhrase(passPhrase, false), true);
        }

        /// <remarks>
        /// The passphrase is encoded to bytes using UTF8 (Encoding.UTF8.GetBytes).
        /// </remarks>
        public static KeyParameter MakeKeyFromPassPhraseUtf8(SymmetricKeyAlgorithmTag algorithm, S2k s2k,
            char[] passPhrase)
        {
            return DoMakeKeyFromPassPhrase(algorithm, s2k, EncodePassPhrase(passPhrase, true), true);
        }

        /// <remarks>
        /// Allows the caller to handle the encoding of the passphrase to bytes.
        /// </remarks>
        public static KeyParameter MakeKeyFromPassPhraseRaw(SymmetricKeyAlgorithmTag algorithm, S2k s2k,
            byte[] rawPassPhrase)
        {
            return DoMakeKeyFromPassPhrase(algorithm, s2k, rawPassPhrase, false);
        }

        internal static KeyParameter DoMakeKeyFromPassPhrase(SymmetricKeyAlgorithmTag algorithm, S2k s2k,
            byte[] rawPassPhrase, bool clearPassPhrase)
        {
            int keySize = GetKeySize(algorithm);
            byte[] pBytes = rawPassPhrase;
            byte[] keyBytes = new byte[(keySize + 7) / 8];

            if (s2k == null)
            {
                s2k = LegacyS2k;
            }

            if (S2k.Argon2 == s2k.Type)
            {
                var argon2Config = s2k.Argon2Config;
                var argon2Parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
                    .WithSalt(argon2Config.GetSalt())
                    .WithIterations(argon2Config.Passes)
                    .WithParallelism(argon2Config.Parallelism)
                    .WithMemoryPowOfTwo(argon2Config.MemorySizeExponent)
                    .WithVersion(Argon2Parameters.Version13)
                    .Build();

                // TODO Consider ways to customize how Argon2 generation is done
                Argon2BytesGenerator argon2 = new Argon2BytesGenerator(Task.Factory);
                argon2.Init(argon2Parameters);
                argon2.GenerateBytes(rawPassPhrase, keyBytes);

                return MakeKey(algorithm, keyBytes);
            }

            IDigest digest;
            try
            {
                digest = CreateDigest(s2k.HashAlgorithm);
            }
            catch (Exception e)
            {
                throw new PgpException($"can't create {GetDigestName(s2k.HashAlgorithm)} digest", e);
            }

            int generatedBytes = 0, loopCount = 0;

            while (generatedBytes < keyBytes.Length)
            {
                for (int i = 0; i < loopCount; ++i)
                {
                    digest.Update(0);
                }

                switch (s2k.Type)
                {
                case S2k.Simple:
                {
                    digest.BlockUpdate(pBytes, 0, pBytes.Length);
                    break;
                }
                case S2k.Salted:
                {
                    byte[] iv = s2k.GetIV();
                    digest.BlockUpdate(iv, 0, iv.Length);
                    digest.BlockUpdate(pBytes, 0, pBytes.Length);
                    break;
                }
                case S2k.SaltedAndIterated:
                {
                    byte[] iv = s2k.GetIV();
                    digest.BlockUpdate(iv, 0, iv.Length);
                    digest.BlockUpdate(pBytes, 0, pBytes.Length);

                    long count = s2k.IterationCount - iv.Length - pBytes.Length;
                    while (count > 0)
                    {
                        if (count <= iv.Length)
                        {
                            digest.BlockUpdate(iv, 0, (int)count);
                            break;
                        }

                        digest.BlockUpdate(iv, 0, iv.Length);
                        count -= iv.Length;

                        if (count <= pBytes.Length)
                        {
                            digest.BlockUpdate(pBytes, 0, (int)count);
                            break;
                        }

                        digest.BlockUpdate(pBytes, 0, pBytes.Length);
                        count -= pBytes.Length;
                    }
                    break;
                }
                default:
                    throw new PgpException("unknown S2k type: " + s2k.Type);
                }

                byte[] dig = DigestUtilities.DoFinal(digest);
                int toCopy = System.Math.Min(dig.Length, keyBytes.Length - generatedBytes);
                Array.Copy(dig, 0, keyBytes, generatedBytes, toCopy);
                generatedBytes += toCopy;

                loopCount++;
            }

            if (clearPassPhrase && rawPassPhrase != null)
            {
                Array.Clear(rawPassPhrase, 0, rawPassPhrase.Length);
            }

            return MakeKey(algorithm, keyBytes);
        }

        /// <summary>Write out the passed in file as a literal data packet.</summary>
        public static void WriteFileToLiteralData(Stream output, char fileType, FileInfo file)
        {
            PgpLiteralDataGenerator lData = new PgpLiteralDataGenerator();
            using (var pOut = lData.Open(output, fileType, file.Name, file.Length, file.LastWriteTime))
            {
                PipeFileContents(file, pOut);
            }
        }

        /// <summary>Write out the passed in file as a literal data packet in partial packet format.</summary>
        public static void WriteFileToLiteralData(Stream output, char fileType, FileInfo file, byte[] buffer)
        {
            PgpLiteralDataGenerator lData = new PgpLiteralDataGenerator();
            using (var pOut = lData.Open(output, fileType, file.Name, file.LastWriteTime, buffer))
            {
                PipeFileContents(file, pOut, buffer.Length);
            }
        }

        private static void PipeFileContents(FileInfo file, Stream pOut) =>
            PipeFileContents(file, pOut, Streams.DefaultBufferSize);

        private static void PipeFileContents(FileInfo file, Stream pOut, int bufferSize)
        {
            using (var fileStream = file.OpenRead())
            {
                Streams.CopyTo(fileStream, pOut, bufferSize);
            }
        }

        private const int ReadAhead = 60;

        private static bool IsPossiblyBase64(int ch)
        {
            return (ch >= 'A' && ch <= 'Z')
                || (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9')
                || (ch == '+')
                || (ch == '/')
                || (ch == '\r')
                || (ch == '\n');
        }

        /// <summary>
        /// Return either an ArmoredInputStream or a BcpgInputStream based on whether
        /// the initial characters of the stream are binary PGP encodings or not.
        /// </summary>
        public static Stream GetDecoderStream(Stream inputStream)
        {
            // TODO Remove this restriction?
            if (!inputStream.CanSeek)
                throw new ArgumentException("inputStream must be seek-able", nameof(inputStream));

            long markedPos = inputStream.Position;

            int ch = inputStream.ReadByte();
            if ((ch & 0x80) != 0)
                return AtPosition(inputStream, markedPos);

            if (!IsPossiblyBase64(ch))
                return new ArmoredInputStream(AtPosition(inputStream, markedPos));

            byte[] buf = new byte[ReadAhead];
            int count = 0;
            int index = 1;

            buf[0] = (byte)ch;
            while (++count != ReadAhead && (ch = inputStream.ReadByte()) >= 0)
            {
                if (!IsPossiblyBase64(ch))
                    return new ArmoredInputStream(AtPosition(inputStream, markedPos));

                if (ch != '\n' && ch != '\r')
                {
                    buf[index++] = (byte)ch;
                }
            }

            inputStream.Position = markedPos;

            //
            // nothing but new lines, little else, assume regular armoring
            //
            if (count < 4)
                return new ArmoredInputStream(inputStream);

            //
            // test our non-blank data
            //
            Debug.Assert(buf.Length >= 8);

            try
            {
                byte[] decoded = Base64.Decode(buf, 0, 8);

                //
                // it's a base64 PGP block.
                //
                bool hasHeaders = (decoded[0] & 0x80) == 0;

                return new ArmoredInputStream(inputStream, hasHeaders);
            }
            catch (IOException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new IOException(e.Message);
            }
        }

        internal static IDigest CreateDigest(HashAlgorithmTag hashAlgorithm) =>
            DigestUtilities.GetDigest(GetDigestName(hashAlgorithm));

        internal static ISigner CreateSigner(PublicKeyAlgorithmTag publicKeyAlgorithm, HashAlgorithmTag hashAlgorithm,
            AsymmetricKeyParameter key)
        {
            switch (publicKeyAlgorithm)
            {
            case PublicKeyAlgorithmTag.EdDsa_Legacy:
            {
                ISigner signer;
                if (key is Ed25519PrivateKeyParameters || key is Ed25519PublicKeyParameters)
                {
                    signer = new Ed25519Signer();
                }
                else if (key is Ed448PrivateKeyParameters || key is Ed448PublicKeyParameters)
                {
                    signer = new Ed448Signer(Arrays.EmptyBytes);
                }
                else
                {
                    throw new InvalidOperationException();
                }

                return new EdDsaSigner(signer, CreateDigest(hashAlgorithm));
            }
            default:
            {
                return SignerUtilities.GetSigner(GetSignatureName(publicKeyAlgorithm, hashAlgorithm));
            }
            }
        }

        internal static IWrapper CreateWrapper(SymmetricKeyAlgorithmTag encAlgorithm)
        {
            switch (encAlgorithm)
            {
            case SymmetricKeyAlgorithmTag.Aes128:
            case SymmetricKeyAlgorithmTag.Aes192:
            case SymmetricKeyAlgorithmTag.Aes256:
                return WrapperUtilities.GetWrapper("AESWRAP");
            case SymmetricKeyAlgorithmTag.Camellia128:
            case SymmetricKeyAlgorithmTag.Camellia192:
            case SymmetricKeyAlgorithmTag.Camellia256:
                return WrapperUtilities.GetWrapper("CAMELLIAWRAP");
            default:
                throw new PgpException("unknown wrap algorithm: " + encAlgorithm);
            }
        }

        private static Stream AtPosition(Stream stream, long position)
        {
            stream.Position = position;
            return stream;
        }
    }
}
