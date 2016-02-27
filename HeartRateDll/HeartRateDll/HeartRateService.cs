using System;
using System.Collections.Generic;
using System.Threading;

using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace HeartRateDll
{

    public delegate void NewHeartrateMeasurement(HeartrateMeasurement HeartrateMeasurement);

    /// <summary>
    /// https://developer.bluetooth.org/gatt/characteristics/Pages/CharacteristicViewer.aspx?u=org.bluetooth.characteristic.heart_rate_measurement.xml
    /// </summary>
    public class HeartrateMeasurement
    {
        public const Byte HEART_RATE_VALUE_FORMAT_UINT32 = 0x01;
        public const Byte SENSOR_CONTACT_NOT_SUPPORTED = 0x02;
        public const Byte SENSOR_CONTACT_SUPPORTED_NOT_DETECTED = 0x04;
        public const Byte SENSOR_CONTACT_SUPPORTED_DETECTED = 0x06;
        public const Byte ENERGY_EXPANDED_FIELD_PRESENT = 0x08;
        public const Byte INTERVAL_FIELD_PRESENT = 0x10;

        public UInt16 HeartRateValue { get; private set; }
        public Boolean HasExpendedEnergy { get; private set; }
        public UInt16 ExpendedEnergy { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }

        public HeartrateMeasurement(UInt16 HeartRateValue, Boolean HasExpendedEnergy, UInt16 ExpendedEnergy, DateTimeOffset Timestamp)
        {
            this.HeartRateValue = HeartRateValue;
            this.HasExpendedEnergy = HasExpendedEnergy;
            this.ExpendedEnergy = ExpendedEnergy;
            this.Timestamp = Timestamp;
        }

        public override string ToString()
        {
            return String.Format(@"{0} bpm @ {1} ", HeartRateValue, Timestamp.ToString());
        }
    }

    public class HeartRateService : IDisposable
    {
        /*private static void Main()
        {
            HeartRateService HeartRateService = new HeartRateService();
            HeartRateService.NewHeartrateMeasurement += HeartRateService_NewHeartrateMeasurement;
            Thread.Sleep(30000);
            HeartRateService.Dispose();
        }

        private static void HeartRateService_NewHeartrateMeasurement(HeartrateMeasurement HeartrateMeasurement)
        {
            Console.WriteLine(HeartrateMeasurement);
        }*/

        private const UInt16 CHARACTERISTIC_INDEX = 0;

        public event NewHeartrateMeasurement NewHeartrateMeasurement;

        private GattDeviceService mService;
        private GattCharacteristic mCharacteristic;

        public HeartRateService()
        {
            IAsyncOperation<DeviceInformationCollection> devices = DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.HeartRate), new [] { @"System.Devices.ContainerId" });
            devices.Completed = FindServices;
        }

        public void Dispose()
        {
            mService.Dispose();
        }

        private void FindServices(IAsyncOperation<DeviceInformationCollection> AsyncInfo, AsyncStatus AsyncStatus)
        {
            DeviceInformationCollection dic = AsyncInfo.GetResults();
            if (dic.Count == 0) throw new Exception(@"Cannot find heartrate monitor.");
            IAsyncOperation<GattDeviceService> services = GattDeviceService.FromIdAsync(dic[0].Id);
            services.Completed = ConfigureServices;
        }

        private void ConfigureServices(IAsyncOperation<GattDeviceService> AsyncInfo, AsyncStatus AsyncStatus)
        {
            mService = AsyncInfo.GetResults();
            mCharacteristic = mService.GetCharacteristics(GattCharacteristicUuids.HeartRateMeasurement)[CHARACTERISTIC_INDEX];
            mCharacteristic.ProtectionLevel = GattProtectionLevel.EncryptionRequired;
            mCharacteristic.ValueChanged += CharacteristicValueChanged;

            IAsyncOperation<GattReadClientCharacteristicConfigurationDescriptorResult> characteristicConfiguration = mCharacteristic.ReadClientCharacteristicConfigurationDescriptorAsync();
            characteristicConfiguration.Completed = CharacteristicConfiguration;
        }

        private void CharacteristicConfiguration(IAsyncOperation<GattReadClientCharacteristicConfigurationDescriptorResult> AsyncInfo, AsyncStatus AsyncStatus)
        {
            GattReadClientCharacteristicConfigurationDescriptorResult characteristicConfiguration = AsyncInfo.GetResults();
            if (characteristicConfiguration.Status != GattCommunicationStatus.Success || characteristicConfiguration.ClientCharacteristicConfigurationDescriptor != GattClientCharacteristicConfigurationDescriptorValue.Notify)
            {
                IAsyncOperation<GattCommunicationStatus> communicationStatus = mCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                communicationStatus.Completed = CommunicationStatus;
            }
        }

        private void CommunicationStatus(IAsyncOperation<GattCommunicationStatus> AsyncInfo, AsyncStatus AsyncStatus)
        {
            GattCommunicationStatus communicationStatus = AsyncInfo.GetResults();
            if (communicationStatus != GattCommunicationStatus.Success) throw new Exception(@"Cannot establish connection to heartrate monitor.");
        }

        private void CharacteristicValueChanged(GattCharacteristic Sender, GattValueChangedEventArgs EventArgs)
        {
            Byte[] data = new Byte[EventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(EventArgs.CharacteristicValue).ReadBytes(data);

            HeartrateMeasurement value = ProcessData(data, EventArgs.Timestamp);
            if (NewHeartrateMeasurement != null) NewHeartrateMeasurement(value);
        }

        private HeartrateMeasurement ProcessData(Byte[] DatapointRaw, DateTimeOffset Timestamp)
        {
            Byte currentOffset = 0;
            Byte flags = DatapointRaw[currentOffset];
            Boolean isHeartRateValueSizeLong = (flags & HeartrateMeasurement.HEART_RATE_VALUE_FORMAT_UINT32) != 0;
            Boolean hasEnergyExpended = (flags & HeartrateMeasurement.ENERGY_EXPANDED_FIELD_PRESENT) != 0;

            currentOffset++;

            UInt16 heartRateMeasurementValue = 0;

            heartRateMeasurementValue = (isHeartRateValueSizeLong) ? (UInt16)((DatapointRaw[currentOffset + 1] << 8) + DatapointRaw[currentOffset]) : DatapointRaw[currentOffset];
            heartRateMeasurementValue += (isHeartRateValueSizeLong) ? (UInt16)2 : (UInt16)1;

            UInt16 expendedEnergyValue = 0;

            if (hasEnergyExpended)
            {
                expendedEnergyValue = (UInt16)((DatapointRaw[currentOffset + 1] << 8) + DatapointRaw[currentOffset]);
                currentOffset += 2;
            }

            return new HeartrateMeasurement(heartRateMeasurementValue, hasEnergyExpended, expendedEnergyValue, Timestamp);
        }
    }
}
