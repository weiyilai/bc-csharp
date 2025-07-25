using System.IO;

using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.IO;

namespace Org.BouncyCastle.Bcpg
{
    /// <summary>Basic packet for a PGP public key.</summary>
    public class PublicKeyEncSessionPacket
        : ContainedPacket //, PublicKeyAlgorithmTag
    {
        private readonly int m_version;
        private readonly ulong m_keyID;
        private readonly PublicKeyAlgorithmTag m_algorithm;
        private readonly byte[][] m_data;

        internal PublicKeyEncSessionPacket(BcpgInputStream bcpgIn)
        {
            m_version = bcpgIn.RequireByte();
            m_keyID = StreamUtilities.RequireUInt64BE(bcpgIn);
            m_algorithm = (PublicKeyAlgorithmTag)bcpgIn.RequireByte();

            switch (m_algorithm)
            {
            case PublicKeyAlgorithmTag.RsaEncrypt:
            case PublicKeyAlgorithmTag.RsaGeneral:
                m_data = new byte[][]{ new MPInteger(bcpgIn).GetEncoded() };
                break;
            case PublicKeyAlgorithmTag.ElGamalEncrypt:
            case PublicKeyAlgorithmTag.ElGamalGeneral:
                MPInteger p = new MPInteger(bcpgIn);
                MPInteger g = new MPInteger(bcpgIn);
                m_data = new byte[][]{
                    p.GetEncoded(),
                    g.GetEncoded(),
                };
                break;
            case PublicKeyAlgorithmTag.ECDH:
                m_data = new byte[][]{ Streams.ReadAll(bcpgIn) };
                break;
            default:
                throw new IOException("unknown PGP public key algorithm encountered");
            }
        }

        public PublicKeyEncSessionPacket(long keyId, PublicKeyAlgorithmTag algorithm, byte[][] data)
        {
            m_version = 3;
            m_keyID = (ulong)keyId;
            m_algorithm = algorithm;
            m_data = new byte[data.Length][];
            for (int i = 0; i < data.Length; ++i)
            {
                m_data[i] = Arrays.Clone(data[i]);
            }
        }

        public int Version => m_version;

        /// <remarks>
        /// A Key ID is an 8-octet scalar. We convert it (big-endian) to an Int64 (UInt64 is not CLS compliant).
        /// </remarks>
        public long KeyId => (long)m_keyID;

        public PublicKeyAlgorithmTag Algorithm => m_algorithm;

        public byte[][] GetEncSessionKey() => m_data;

        public override void Encode(BcpgOutputStream bcpgOut)
        {
            MemoryStream bOut = new MemoryStream();

            bOut.WriteByte((byte)m_version);
            StreamUtilities.WriteUInt64BE(bOut, m_keyID);
            bOut.WriteByte((byte)m_algorithm);

            foreach (var data in m_data)
            {
                bOut.Write(data, 0, data.Length);
            }

            bcpgOut.WritePacket(PacketTag.PublicKeyEncryptedSession, bOut.ToArray());
        }
    }
}
