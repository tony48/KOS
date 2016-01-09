﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Serialization;

namespace kOS.Safe.Encapsulation
{
    public abstract class EnumerableValue<T, TC> : Structure, IEnumerable<T>, IDumper where TC : IEnumerable<T>
    {
        protected TC Collection { get; private set; }

        protected EnumerableValue(TC collection)
        {
            Collection = collection;

            InitializeEnumerableSuffixes();
        }

        public virtual IEnumerator<T> GetEnumerator()
        {
            return Collection.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool Contains(T item)
        {
            return Collection.Contains(item);
        }

        public abstract int Count { get; }

        public override string ToString()
        {
            return new SafeSerializationMgr().ToString(this);
        }

        public abstract Dump Dump();

        public abstract void LoadDump(Dump dump);

        private void InitializeEnumerableSuffixes()
        {
            AddSuffix("ITERATOR",   new NoArgsSuffix<Enumerator>          (() => new Enumerator (Collection.GetEnumerator())));
            AddSuffix("CONTAINS",   new OneArgsSuffix<bool, T>            (item => Collection.Contains(item)));
            AddSuffix("EMPTY",      new NoArgsSuffix<bool>                (() => !Collection.Any()));
            AddSuffix("DUMP",       new NoArgsSuffix<string>              (ToString));
        }
    }
}

