using System.IO;
using System.Threading.Tasks;
using Azure.Sdk.tools.TestProxy.Common;
using Azure.Sdk.Tools.TestProxy.Console;

namespace Azure.Sdk.Tools.TestProxy.Store
{
    public interface IAssetsStore
    {
        /// <summary>
        /// Given a configuration, push the changes made by the test-proxy into the remote store.
        /// </summary>
        /// <param name="pathToAssetsJson"></param>
        /// <param name="ignoreSecretProtection"></param>
        /// <returns>An integer representing the status of the push command.</returns>
        public abstract Task<int> Push(string pathToAssetsJson, bool ignoreSecretProtection = false);

        /// <summary>
        /// Given a configuration, pull any remote resources down into the provided contextPath.
        /// </summary>
        /// <param name="pathToAssetsJson"></param>
        public abstract Task<string> Restore(string pathToAssetsJson);

        /// <summary>
        /// Given a configuration, determine the state of the resources present under contextPath, reset those resources to their "fresh" state.
        /// </summary>
        /// <param name="pathToAssetsJson"></param>
        public abstract Task Reset(string pathToAssetsJson);

        /// <summary>
        /// Given a configuration, return the path on disk to the root of the cloned repo.
        /// </summary>
        /// <param name="pathToAssetsJson"></param>
        /// <returns></returns>
        public abstract Task<NormalizedString> GetPath(string pathToAssetsJson);

        /// <summary>
        /// Set the mode of the store to throw exceptions or to simply early exit for CLI mode.
        /// </summary>
        /// <param name="throwOnException"></param>
        /// <returns></returns>
        public abstract void SetStoreExceptionMode(bool throwOnException);
    }
}
