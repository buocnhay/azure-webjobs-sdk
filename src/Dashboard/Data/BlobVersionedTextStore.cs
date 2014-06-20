﻿using System;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    // An IVersionedTextStore implemented using a blob container in Azure Storage.
    public class BlobVersionedTextStore : IVersionedTextStore
    {
        private readonly CloudBlobContainer _container;

        [CLSCompliant(false)]
        public BlobVersionedTextStore(CloudBlobContainer container)
        {
            _container = container;
        }

        public VersionedText Read(string id)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);
            string text;

            try
            {
                text = blob.DownloadText();
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }

            string eTag = blob.Properties.ETag;
            return new VersionedText(text, eTag);
        }

        public void CreateOrUpdate(string id, string text)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);

            try
            {
                blob.UploadText(text);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    _container.CreateIfNotExists();
                    blob.UploadText(text);
                }
                else
                {
                    throw;
                }
            }
        }

        public void DeleteIfExists(string id)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);

            blob.DeleteIfExists();
        }

        public bool TryCreate(string id, string text)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);
            _container.CreateIfNotExists();

            return TryUploadTextAndCreateContainerIfNotExists(blob, text, new AccessCondition { IfNoneMatchETag = "*" });
        }

        public bool TryUpdate(string id, string text, string eTag)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);

            return TryUploadTextAndCreateContainerIfNotExists(blob, text, new AccessCondition { IfMatchETag = eTag });
        }

        public bool TryDelete(string id, string eTag)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);

            try
            {
                blob.Delete(accessCondition: new AccessCondition { IfMatchETag = eTag });
            }
            catch (StorageException exception)
            {
                // The item may have already been deleted (409) or updated by someone else (412), or (unusual) the container
                // may have been deleted (404).
                if (exception.IsConflict() || exception.IsPreconditionFailed() || exception.IsNotFound())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }

            return true;
        }

        private bool TryUploadTextAndCreateContainerIfNotExists(CloudBlockBlob blob, string contents,
            AccessCondition accessCondition)
        {
            try
            {
                blob.UploadText(contents, accessCondition: accessCondition);
            }
            catch (StorageException exception)
            {
                if (exception.IsPreconditionFailed() || exception.IsConflict())
                {
                    return false;
                }
                else if (exception.IsNotFound())
                {
                    _container.CreateIfNotExists();

                    try
                    {
                        blob.UploadText(contents, accessCondition: accessCondition);
                    }
                    catch (StorageException retryException)
                    {
                        if (retryException.IsPreconditionFailed() || exception.IsConflict())
                        {
                            return false;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                else
                {
                    throw;
                }
            }

            return true;
        }
    }
}