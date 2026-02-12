namespace IIoTVale.Backend.API.Services
{
    public class SignalProcessingService
    {
        public struct FrequencyBin
        {
            public double Frequency;
            public double Magnitude;
        }

        /// <summary>
        /// Computes the FFT for a given data buffer.
        /// Replicates the logic of removing DC offset, applying Hanning window, and Cooley-Tukey algorithm.
        /// </summary>
        /// <param name="buffer">Raw sensor data array.</param>
        /// <param name="sampleRate">Sampling rate in Hz.</param>
        /// <returns>List of frequency bins with magnitude.</returns>
        public static List<FrequencyBin> ComputeFft(double[] buffer, double sampleRate)
        {
            int N = buffer.Length;
            if (N == 0)
            {
                return new List<FrequencyBin>();
            }

            // Ensure N is power of 2 for Cooley-Tukey
            // Truncate to the nearest lower power of 2
            int powerOf2 = 1 << (int)Math.Floor(Math.Log(N, 2));

            // Take the most recent samples (matching logic: input = buffer.slice(N - powerOf2, N))
            double[] input = new double[powerOf2];
            Array.Copy(buffer, N - powerOf2, input, 0, powerOf2);

            int n = powerOf2;

            // 0. Remove DC Offset (Mean) to eliminate 0Hz spike (Gravity)
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                sum += input[i];
            }
            double mean = sum / n;

            // 1. Apply Hanning Window & Prepare Arrays
            // Window Correction Factor (Amplitude) = 2 for Hanning
            // FFT Normalization = 2/N
            // Total Scaling = 4/N
            double[] real = new double[n];
            double[] imag = new double[n]; // Initialized to 0.0 by default

            for (int i = 0; i < n; i++)
            {
                double zeroMeanVal = input[i] - mean;
                double w = 0.5 * (1.0 - Math.Cos((2.0 * Math.PI * i) / (n - 1)));
                real[i] = zeroMeanVal * w;
            }

            // 2. Cooley-Tukey FFT (Radix-2)
            // Bit-reversal permutation
            int j = 0;
            for (int i = 0; i < n - 1; i++)
            {
                if (i < j)
                {
                    // Swap real
                    double tr = real[i];
                    real[i] = real[j];
                    real[j] = tr;

                    // Swap imag
                    double ti = imag[i];
                    imag[i] = imag[j];
                    imag[j] = ti;
                }

                int k = n >> 1;
                while (k <= j)
                {
                    j -= k;
                    k >>= 1;
                }
                j += k;
            }

            // Butterfly operations
            for (int m = 1; m < n; m <<= 1) // m = 1, 2, 4, 8...
            {
                int step = m << 1; // 2, 4, 8...

                // W_m = e^(-j * PI / m)
                double wr_step = Math.Cos(-Math.PI / m);
                double wi_step = Math.Sin(-Math.PI / m);

                for (int k = 0; k < n; k += step)
                {
                    double wr = 1.0;
                    double wi = 0.0;

                    for (int i = 0; i < m; i++)
                    {
                        int indexJ = k + i;
                        int indexJp = indexJ + m;

                        double tr = (wr * real[indexJp]) - (wi * imag[indexJp]);
                        double ti = (wr * imag[indexJp]) + (wi * real[indexJp]);

                        real[indexJp] = real[indexJ] - tr;
                        imag[indexJp] = imag[indexJ] - ti;
                        real[indexJ] = real[indexJ] + tr;
                        imag[indexJ] = imag[indexJ] + ti;

                        // Rotate W
                        double old_wr = wr;
                        wr = (wr * wr_step) - (wi * wi_step);
                        wi = (old_wr * wi_step) + (wi * wr_step);
                    }
                }
            }

            // 3. Compute Magnitude and Normalize
            List<FrequencyBin> spectrum = new List<FrequencyBin>();
            int kLimit = (int)Math.Floor(n / 2.0);
            double scalingFactor = 4.0 / n; // Normalization for Hanning Window + FFT

            for (int k = 0; k < kLimit; k++)
            {
                double mag = Math.Sqrt((real[k] * real[k]) + (imag[k] * imag[k]));

                spectrum.Add(new FrequencyBin
                {
                    Frequency = k * sampleRate / n,
                    Magnitude = mag * scalingFactor
                });
            }

            return spectrum;
        }
    }
}
